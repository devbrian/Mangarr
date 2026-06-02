using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-03) — see DIVERGENCE.md.
    // Provenance (control-flow): v5-develop:src/NzbDrone.Core/Download/CompletedDownloadService.cs
    //   (the TV Check(TrackedDownload) → Import dispatch shape; manga substitutes the in-process
    //   image-archive output for the torrent/usenet payload). NO live TV peer at HEAD — the Tv/
    //   CompletedDownloadService was removed in the Phase 15 cutover.
    // EXTRACT-analog: src/NzbDrone.Core/Download/Manga/ProcessMangaCompletedDownloads.ProcessOne
    //   (the idempotent import core lifted here and GENERALIZED off the matched TrackedDownload
    //   instead of the in-process ChapterDownloadState row).
    //
    // ============================================================================
    // GENERALIZED IMPORT CORE (the 3 inputs that vanish with ChapterDownloadState are re-sourced
    // from the matched TrackedDownload — Plan 03 — instead, NO JSON round-trip):
    //
    //   staging path  ← trackedDownload.DownloadItem.OutputPath           (was state.StagingPath)
    //   chapterIds    ← trackedDownload.RemoteChapter.Chapters[*].Id      (was state.ChapterId)
    //   provenance    ← trackedDownload.RemoteChapter.Release (in-memory) (was TryRecoverGrabbedRelease
    //                                                                       JSON deserialize)
    //
    // KEPT VERBATIM from ProcessOne: the GetFilesByChapter idempotency short-circuit; the
    // LocalChapter construction with NON-BLANK TranslatedLanguage/ScanlationGroup; the
    // IMakeMangaImportDecision.GetDecision → IImportApprovedChapters.Import dispatch.
    //
    // PITFALL-4 ORDERING: ChapterDownloadCompletedEvent (Plan 01) is published ONLY AFTER
    // IImportApprovedChapters.Import returns successfully — never before, or the Komga/Kavita
    // rescan handlers (which fire on the import pipeline's own ChapterImportedEvent) would race a
    // half-written library.
    //
    // NOTE: ProcessMangaCompletedDownloads is intentionally NOT edited in place to read the
    // TrackedDownload — its hybrid IHandle<ChapterArchivedEvent> + IExecute<ProcessMangaCompletedCommand>
    // in-process path keeps driving off ChapterDownloadState/StagingPath (and keeps its scratch-dir
    // cleanup) through Phase 38. This service is the OutputPath-driven generalization that the
    // monitoring loop (Plan 03 matcher) feeds.
    //
    // Phase 38 cleanup: when the gateway path lands, this becomes the canonical completed-download
    // import entry; the in-process ProcessMangaCompletedDownloads collapses into it.
    // ============================================================================
    public interface IMangaCompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);

        // Returns true ONLY when the chapter was actually imported (decision approved + dispatched).
        // Returns false on every short-circuit/rejection: null RemoteChapter, missing staging path,
        // unresolved manga/chapter, an already-existing ChapterFile (idempotency), or a rejected
        // decision. The caller (MangaDownloadProcessingService) uses this to decide whether to flip
        // the row to Imported (eligible for deleteData:true eviction) or leave it in place for retry
        // (WR-05 — mirrors Sonarr CompletedDownloadService.Import/VerifyImport state ownership: only
        // mark Imported when ALL items actually imported, else leave ImportPending for retry).
        bool Import(TrackedDownload trackedDownload);
    }

    public class MangaCompletedDownloadService : IMangaCompletedDownloadService
    {
        private readonly IChapterFileService _chapterFileService;
        private readonly IMakeMangaImportDecision _decisionMaker;
        private readonly IImportApprovedChapters _importer;
        private readonly IDiskProvider _diskProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaCompletedDownloadService(
            IChapterFileService chapterFileService,
            IMakeMangaImportDecision decisionMaker,
            IImportApprovedChapters importer,
            IDiskProvider diskProvider,
            IEventAggregator eventAggregator,
            Logger logger)
        {
            _chapterFileService = chapterFileService;
            _decisionMaker = decisionMaker;
            _importer = importer;
            _diskProvider = diskProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // Completed item with a non-empty OutputPath is "ready for import" → transition the tracked
        // download into ImportPending so the loop's Import(...) pass picks it up. Mirrors the TV
        // CompletedDownloadService Check gate (Completed + reachable path → ImportPending).
        public void Check(TrackedDownload trackedDownload)
        {
            var item = trackedDownload.DownloadItem;
            if (item == null || item.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            if (item.OutputPath.IsEmpty)
            {
                _logger.Debug(
                    "Completed download {0} has no OutputPath; not ready for import",
                    item.DownloadId);
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public bool Import(TrackedDownload trackedDownload)
        {
            var remoteChapter = trackedDownload.RemoteChapter;
            if (remoteChapter == null)
            {
                _logger.Warn(
                    "Completed download {0} has no resolved RemoteChapter; cannot import",
                    trackedDownload.DownloadItem?.DownloadId);
                return false;
            }

            // Re-source the staging path from the matched TrackedDownload (was state.StagingPath).
            var stagingPath = trackedDownload.DownloadItem?.OutputPath.FullPath;
            if (string.IsNullOrEmpty(stagingPath) || !_diskProvider.FileExists(stagingPath))
            {
                _logger.Warn(
                    "Staging path missing for download {0}: {1}",
                    trackedDownload.DownloadItem?.DownloadId,
                    stagingPath ?? "<null>");
                return false;
            }

            // Re-source chapter ids from the in-memory RemoteChapter projection (was state.ChapterId).
            var chapters = remoteChapter.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>();
            var chapter = chapters.FirstOrDefault();
            if (chapter == null || remoteChapter.Manga == null)
            {
                _logger.Warn(
                    "Download {0} resolved no manga/chapter; cannot import",
                    trackedDownload.DownloadItem?.DownloadId);
                return false;
            }

            // ── Idempotency short-circuit (CR-b — ALL chapters, not just the first) ─────
            // Only short-circuit the WHOLE tracked download when EVERY chapter in the pack
            // already has a ChapterFile. For a multi-chapter pack where only a subset have
            // files, short-circuiting on chapters.First() would falsely mark the row fully
            // imported and evict the scratch data for the not-yet-imported chapters. When only
            // a subset have files we proceed to import — the import pipeline's
            // ChapterFileExistsSpecification (Chapter.ChapterFileId > 0) + the in-batch dedupe
            // in ImportApprovedChapters skip the already-imported chapters without double-import.
            if (chapters.All(c => _chapterFileService.GetFilesByChapter(c.Id).Any()))
            {
                _logger.Debug(
                    "All {0} chapter(s) for download {1} already have a ChapterFile; skipping import",
                    chapters.Count,
                    trackedDownload.DownloadItem?.DownloadId);

                // Already fully imported by a prior pass — the row IS legitimately importable/removable.
                // Returning true lets the caller flip it to Imported so the now-redundant scratch
                // data is evicted (the idempotency win is the import-already-happened case).
                return true;
            }

            // ── Build the LocalChapter aggregate with NON-BLANK provenance ──────────────
            // Provenance comes from the in-memory RemoteChapter.Release (Plan 03 matcher
            // populated it) — NO ChapterDownloadState JSON deserialize / TryRecoverGrabbedRelease.
            var release = remoteChapter.Release ?? new ReleaseInfo();

            var localChapter = new LocalChapter
            {
                Path = stagingPath,
                Size = SafeGetFileSize(stagingPath),
                Manga = remoteChapter.Manga,
                Chapter = chapter,
                Chapters = chapters.ToList(),
                TranslatedLanguage = release.TranslatedLanguage,
                ScanlationGroup = release.ScanlationGroup,
                Release = release
            };

            // ── Run the manga import-spec set via the KEPT decision maker ───────────────
            var decision = _decisionMaker.GetDecision(localChapter, downloadClientItem: null);
            if (!decision.Approved)
            {
                _logger.Info(
                    "Chapter {0} import rejected: {1}",
                    chapter.Id,
                    string.Join("; ", decision.Rejections.Select(r => r.Message)));

                // WR-05: a rejected decision (e.g. NotUpgradeAllowed) was NOT imported. Returning
                // false leaves the row in place (Q-8 retention posture) instead of being evicted with
                // deleteData:true and losing the scratch data needed for retry.
                return false;
            }

            // ── Dispatch into the KEPT IImportApprovedChapters pipeline ─────────────────
            // P1: capture the importer results — IImportApprovedChapters.Import RETURNS (never
            // throws) Skipped/Rejected results for destination-exists, missing-root-folder, or
            // move/recycle failures. Honoring them before reporting success mirrors the canonical
            // ProcessMangaCompletedDownloads (if results.All(Imported)) and Sonarr's
            // CompletedDownloadService.VerifyImport.
            var results = _importer.Import(
                new List<MangaImportDecision> { decision },
                newDownload: true,
                downloadClientItem: null);

            if (!results.Any() || !results.All(r => r.Result == MangaImportResultType.Imported))
            {
                _logger.Warn(
                    "Import for download {0} did not import all chapters ({1} result(s)); leaving row for retry",
                    trackedDownload.DownloadItem?.DownloadId,
                    results.Count);

                // P1: the importer Skipped/Rejected (or returned nothing) — NOT a genuine import.
                // Returning false leaves the row ImportPending (Q-8 retention posture) instead of
                // being evicted with deleteData:true and losing the scratch data needed for retry.
                return false;
            }

            // ── PITFALL-4 — publish the completion event LAST, after Import returns ─────
            // ChapterImportedEvent (the rescan-trigger handlers subscribe to) was already
            // published as the last line of ImportApprovedChapters; this completion event
            // (consumed by MangaDownloadHistoryService to write the DownloadImported join row)
            // is published only now so the history row never predates the import.
            _eventAggregator.PublishEvent(
                new ChapterDownloadCompletedEvent(trackedDownload, trackedDownload.DownloadItem?.DownloadId));

            // Genuine import — the caller may now flip the row to Imported and evict it.
            return true;
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
    }
}
