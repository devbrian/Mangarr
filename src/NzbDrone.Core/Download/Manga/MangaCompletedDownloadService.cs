using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-03) — see DIVERGENCE.md.
    // Provenance (control-flow): v5-develop:src/NzbDrone.Core/Download/CompletedDownloadService.cs
    //   (the TV Check(TrackedDownload) → Import dispatch shape; manga substitutes the in-process
    //   image-archive output for the torrent/usenet payload). NO live TV peer at HEAD — the Tv/
    //   CompletedDownloadService was removed in the Phase 15 cutover.
    // EXTRACT-analog: the idempotent import core was lifted from the in-process
    //   ProcessMangaCompletedDownloads.ProcessOne (retired in Phase 39 RETIRE-01) and GENERALIZED
    //   off the matched TrackedDownload instead of the in-process ChapterDownloadState row.
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
    // NOTE: Phase 39 RETIRE-01 deleted the former in-process hybrid completion poller
    // (ProcessMangaCompletedDownloads — IHandle<ChapterArchivedEvent> + IExecute<ProcessMangaCompletedCommand>,
    // driven off the now-retired ChapterDownloadState/StagingPath). This OutputPath-driven service,
    // fed by the Phase 36 monitoring loop (Plan 03 matcher), is now the SOLE completed-download
    // import entry — there is no longer a second in-process path to collapse with.
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
        private readonly IChapterHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaCompletedDownloadService(
            IChapterFileService chapterFileService,
            IMakeMangaImportDecision decisionMaker,
            IImportApprovedChapters importer,
            IDiskProvider diskProvider,
            IChapterHistoryService historyService,
            IEventAggregator eventAggregator,
            Logger logger)
        {
            _chapterFileService = chapterFileService;
            _decisionMaker = decisionMaker;
            _importer = importer;
            _diskProvider = diskProvider;
            _historyService = historyService;
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

                // #319: surface the failure on the queue row (not just the server log) so a wedged
                // "Downloaded - Importing" item shows WHY. trackedDownload.Warn sets Status=Warning +
                // StatusMessages; it does NOT cascade to the failed-download path
                // (MangaFailedDownloadService.Check gates on DownloadItem.Status, not trackedDownload.Status).
                trackedDownload.Warn("Unable to import: no manga/chapter is linked to this download.");
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
                trackedDownload.Warn("Unable to import: the downloaded file is missing at {0}.", stagingPath ?? "<unknown path>");
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
                trackedDownload.Warn("Unable to import: could not resolve the manga or chapter for this download.");
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

                // A re-grab of a chapter you already own short-circuits here BEFORE the upgrade
                // decision runs. Record a (neutral) Ignored history row so the grab's outcome is
                // visible in Activity > History instead of silently leaving only the Grabbed row
                // (debug: reimport-no-history-event). Deduped on download id so the resilience
                // re-poll / restart-replay of an already-evicted import does not re-write it.
                RecordIgnored(
                    trackedDownload,
                    remoteChapter,
                    chapters,
                    "Chapter already imported — not re-importing this download",
                    ImportRejectionReason.ChapterAlreadyImported.ToString());

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
                var rejectionSummary = string.Join("; ", decision.Rejections.Select(r => r.Message));

                _logger.Info(
                    "Chapter {0} import rejected: {1}",
                    chapter.Id,
                    rejectionSummary);

                // #319: surface the rejection reason on the queue row so the user can see WHY the
                // item isn't importing (e.g. NotUpgradeAllowed) instead of a silent stuck state.
                trackedDownload.Warn("Import rejected: {0}", rejectionSummary);

                // Record a (neutral) Ignored history row so the rejected re-grab is visible in
                // Activity > History (debug: reimport-no-history-event). This branch RETURNS FALSE
                // → the row stays ImportPending and is re-driven every poll cycle, so RecordIgnored
                // MUST dedupe on download id or it would write a fresh row every minute.
                RecordIgnored(
                    trackedDownload,
                    remoteChapter,
                    chapters,
                    rejectionSummary,
                    string.Join("; ", decision.Rejections.Select(r => r.Reason.ToString())));

                // WR-05: a rejected decision (e.g. NotUpgradeAllowed) was NOT imported. Returning
                // false leaves the row in place (Q-8 retention posture) instead of being evicted with
                // deleteData:true and losing the scratch data needed for retry.
                return false;
            }

            // ── Dispatch into the KEPT IImportApprovedChapters pipeline ─────────────────
            // P1: capture the importer results — IImportApprovedChapters.Import RETURNS (never
            // throws) Skipped/Rejected results for destination-exists, missing-root-folder, or
            // move/recycle failures. Honoring them before reporting success mirrors the retired
            // in-process ProcessMangaCompletedDownloads (if results.All(Imported); deleted Phase 39
            // RETIRE-01) and Sonarr's CompletedDownloadService.VerifyImport.
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
                trackedDownload.Warn("Import did not complete for all chapters; will retry.");

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

        // Publishes a ChapterImportIgnoredEvent per chapter so ChapterHistoryService writes a neutral
        // Ignored row — skipping any chapter that ALREADY has an Ignored row for this download (dedup).
        // The dedup is load-bearing for the decision-rejection caller, which returns false and is
        // re-driven every poll cycle; without it a non-upgrade rejection would spam a fresh Ignored row
        // every minute. Scoped per (DownloadId, ChapterId) so a multi-chapter pack whose rows were only
        // partially persisted (e.g. a crash mid-write) still records the missing chapters on a later pass.
        private void RecordIgnored(
            TrackedDownload trackedDownload,
            RemoteChapter remoteChapter,
            List<NzbDrone.Core.Manga.Chapter> chapters,
            string reason,
            string rejectionType)
        {
            var downloadId = trackedDownload.DownloadItem?.DownloadId;

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var ignoredChapterIds = _historyService.FindByDownloadId(downloadId)?
                    .Where(h => h.EventType == ChapterHistoryEventType.Ignored)
                    .Select(h => h.ChapterId)
                    .ToHashSet() ?? new HashSet<int>();

                chapters = chapters.Where(c => !ignoredChapterIds.Contains(c.Id)).ToList();
                if (chapters.Count == 0)
                {
                    return;
                }
            }

            var release = remoteChapter.Release;
            var stagingPath = trackedDownload.DownloadItem?.OutputPath.FullPath;

            foreach (var chapter in chapters)
            {
                _eventAggregator.PublishEvent(new ChapterImportIgnoredEvent
                {
                    Manga = remoteChapter.Manga,
                    Chapter = chapter,
                    SourcePath = stagingPath,
                    Reason = reason,
                    RejectionType = rejectionType,
                    Indexer = release?.Indexer,
                    TranslatedLanguage = release?.TranslatedLanguage,
                    ScanlationGroup = release?.ScanlationGroup,
                    DownloadClientItem = trackedDownload.DownloadItem
                });
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
    }
}
