using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-08 + RESEARCH Pattern 1 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/CompletedDownloadService.cs (the TV-side
    // CompletedDownloadService that produces TV import dispatches; Phase 4 D-10 added an
    // early-return guard there for Protocol == DownloadProtocol.Http so the manga sibling
    // can take over the manga-protocol path).
    //
    // ============================================================================
    // PATTERN 1 — HYBRID EVENT-HANDLER + POLLER (RESEARCH lines 430-457):
    //
    //   * IHandle<ChapterArchivedEvent>             — REACTIVE happy path. Phase 4 emits this
    //                                                  on archiver success; we dispatch the
    //                                                  import within milliseconds of completion.
    //
    //   * IExecute<ProcessMangaCompletedCommand>    — RESILIENCE poll path. Registered in
    //                                                  TaskManager.defaultTasks at 1-min cadence.
    //                                                  Covers (a) process restart between archive
    //                                                  completion and import (event lost),
    //                                                  (b) handler exception missed by event
    //                                                  bus, (c) import-spec rejection where
    //                                                  the row is held for retry by Phase 4
    //                                                  housekeeper retention (Q-8).
    //
    //   * IDEMPOTENCY                               — ProcessOne checks ChapterFile already
    //                                                  exists for the chapter id and skips if
    //                                                  so. Both paths can run for the same
    //                                                  chapter without double-importing.
    // ============================================================================
    //
    // Phase 8 cleanup: collapse with TV CompletedDownloadService when Tv/ deletes; the
    // Protocol == DownloadProtocol.Http guard at the TV side disappears with it.
    public class ProcessMangaCompletedDownloads :
        IHandle<ChapterArchivedEvent>,
        IExecute<ProcessMangaCompletedCommand>
    {
        private readonly IChapterDownloadStateRepository _stateRepo;
        private readonly IImportApprovedChapters _importer;
        private readonly IMakeMangaImportDecision _decisionMaker;
        private readonly IChapterFileService _chapterFileService;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ProcessMangaCompletedDownloads(
            IChapterDownloadStateRepository stateRepo,
            IImportApprovedChapters importer,
            IMakeMangaImportDecision decisionMaker,
            IChapterFileService chapterFileService,
            IMangaService mangaService,
            IChapterService chapterService,
            IDiskProvider diskProvider,
            Logger logger)
        {
            _stateRepo = stateRepo;
            _importer = importer;
            _decisionMaker = decisionMaker;
            _chapterFileService = chapterFileService;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Handle(ChapterArchivedEvent message)
        {
            _logger.Debug(
                "Reactive path: ChapterArchivedEvent for chapter {0} (manga {1})",
                message.ChapterId,
                message.MangaId);
            ProcessOne(message.ChapterId, message.StagingPath);
        }

        public void Execute(ProcessMangaCompletedCommand cmd)
        {
            _logger.Debug("Poll path: scanning ChapterDownloadState for completed-ready rows");

            // Phase 4 contract: ChapterDownloadState.Status=Completed AND StagingPath IS NOT NULL
            // is the "ready for import" filter. ByStatus narrows to Completed; the StagingPath
            // null guard inside ProcessOne handles the rest.
            var rows = _stateRepo.ByStatus(ChapterDownloadStatus.Completed).ToList();
            if (rows.Count == 0)
            {
                return;
            }

            _logger.Debug("Poll path found {0} completed-ready rows", rows.Count);

            foreach (var s in rows)
            {
                ProcessOne(s.ChapterId, s.StagingPath);
            }
        }

        private void ProcessOne(int chapterId, string stagingPath)
        {
            // ── 1. Idempotency: if ChapterFile already exists for chapterId, skip. ───────
            // Both reactive and poll paths can fire for the same chapter (e.g., poll lands
            // a millisecond after the IHandle finishes). The check is the contract guard.
            var existingFile = _chapterFileService.GetFilesByChapter(chapterId).FirstOrDefault();
            if (existingFile != null)
            {
                _logger.Debug(
                "Chapter {0} already has ChapterFile at {1}; skipping import",
                chapterId,
                existingFile.Path);
                return;
            }

            // ── 2. Staging path guard ───────────────────────────────────────────────────
            if (string.IsNullOrEmpty(stagingPath) || !_diskProvider.FileExists(stagingPath))
            {
                _logger.Warn("Staging path missing for chapter {0}: {1}", chapterId, stagingPath ?? "<null>");
                return;
            }

            // ── 3. Resolve Manga + Chapter ──────────────────────────────────────────────
            var chapter = _chapterService.GetChapter(chapterId);
            if (chapter == null)
            {
                _logger.Warn("Chapter {0} not found while processing completed download", chapterId);
                return;
            }

            var manga = _mangaService.GetManga(chapter.MangaId);
            if (manga == null)
            {
                _logger.Warn("Manga {0} not found for chapter {1}", chapter.MangaId, chapterId);
                return;
            }

            // ── 4. Build LocalChapter aggregate ─────────────────────────────────────────
            // Recover the original RemoteChapter (and its provenance ReleaseInfo) from the
            // ChapterDownloadState row written at grab time by ChapterDownloadService.EnqueueAsync
            // (RemoteChapterJson = JsonConvert.SerializeObject(remote)). The state row IS the
            // canonical source of truth for in-flight chapter downloads — its lifecycle
            // (Created at grab → Deleted at import success in step 7 below) exactly matches
            // the data flow we need. Without this, ImportApprovedChapters writes ChapterFile
            // rows with empty TranslatedLanguage + ScanlationGroup, and the same fields appear
            // blank on the History "imported" row + the Files tab — even though the grab path
            // had them on the live RemoteChapter.
            //
            // Why state row not history: history is an audit log written AFTER grab succeeds;
            // state is the authoritative in-flight record. State survives history-retention
            // cleanup, is immune to "two grab events on the same chapter" race ordering, and
            // already carries the full RemoteChapter (not just the 5-field subset history
            // happens to copy). On deserialize failure (corrupt JSON / older row shape) we
            // fall back to an empty ReleaseInfo — better to import with blank metadata than
            // to fail the import entirely.
            var state = _stateRepo.FindByMangaAndChapter(manga.Id, chapter.Id);
            var grabbedRelease = TryRecoverGrabbedRelease(state);

            var localChapter = new LocalChapter
            {
                Path = stagingPath,
                Size = SafeGetFileSize(stagingPath),
                Manga = manga,
                Chapter = chapter,
                Chapters = new List<Chapter> { chapter },
                TranslatedLanguage = grabbedRelease?.TranslatedLanguage,
                ScanlationGroup = grabbedRelease?.ScanlationGroup,
                Release = grabbedRelease ?? new ReleaseInfo()
            };

            // ── 5. Run manga import-spec set via decision maker ─────────────────────────
            var decision = _decisionMaker.GetDecision(localChapter, downloadClientItem: null);
            if (!decision.Approved)
            {
                _logger.Info("Chapter {0} import rejected: {1}",
                    chapterId,
                    string.Join("; ", decision.Rejections.Select(r => r.Message)));

                // Q-8 reconciliation: leave ChapterDownloadState row in place — Phase 4 housekeeper
                // (HousekeepInProcessDownloadsCommand) owns the retention sweep. We do NOT delete
                // the row here so the user can manually retry from History (HISTORY-03) within
                // Config.RetentionDays (Phase 4 D-08).
                return;
            }

            // ── 6. Dispatch into ImportApprovedChapters (Plan 06-07) ────────────────────
            var results = _importer.Import(
                new List<MangaImportDecision> { decision },
                newDownload: true,
                downloadClientItem: null);

            // ── 7. On success: lifecycle hook — delete state row + scratch dir ──────────
            //     (Pitfall 4 / D-08 — ImportApprovedChapters already deleted the state row
            //     via _stateRepo.DeleteByChapterId on the success path; calling again is
            //     idempotent. The scratch-dir cleanup is owned here because the importer
            //     does not know the original scratch location.)
            if (results.All(r => r.Result == MangaImportResultType.Imported))
            {
                _stateRepo.DeleteByChapterId(chapterId);

                var scratchDir = Path.GetDirectoryName(stagingPath);
                if (!string.IsNullOrEmpty(scratchDir) && _diskProvider.FolderExists(scratchDir))
                {
                    try
                    {
                        _diskProvider.DeleteFolder(scratchDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        // Pitfall 4 mitigation: a scratch-dir cleanup failure must NOT poison
                        // the import result. Log and continue.
                        _logger.Warn(ex, "Could not delete scratch dir {0}", scratchDir);
                    }
                }
            }
        }

        private long SafeGetFileSize(string path)
        {
            try
            {
                return _diskProvider.GetFileSize(path);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Deserialize the grab-time RemoteChapter from the in-flight state row and return its
        /// Release. Used to propagate provenance metadata (TranslatedLanguage, ScanlationGroup,
        /// SourceTitle, etc.) from the grab into the resulting ChapterFile via LocalChapter.
        /// Returns null if the state row is missing, the JSON is empty/corrupt, or the Release
        /// pointer is null — caller falls back to an empty ReleaseInfo so a recoverable import
        /// proceeds with blank metadata rather than failing the entire import.
        /// </summary>
        private ReleaseInfo TryRecoverGrabbedRelease(ChapterDownloadState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.RemoteChapterJson))
            {
                return null;
            }

            try
            {
                var remote = JsonConvert.DeserializeObject<RemoteChapter>(state.RemoteChapterJson);
                return remote?.Release;
            }
            catch (JsonException ex)
            {
                _logger.Warn(
                    ex,
                    "Could not deserialize RemoteChapterJson for chapter {0} (state row id {1}); importing without provenance metadata",
                    state.ChapterId,
                    state.Id);
                return null;
            }
        }
    }
}
