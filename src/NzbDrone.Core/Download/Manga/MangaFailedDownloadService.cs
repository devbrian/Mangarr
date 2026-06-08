using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-03) — see DIVERGENCE.md.
    // Provenance (control-flow): v5-develop:src/NzbDrone.Core/Download/FailedDownloadService.cs
    //   (the TV Check(TrackedDownload) → ProcessFailed → DownloadFailedEvent dispatch shape).
    //   NO live TV peer at HEAD — removed in the Phase 15 Tv/ cutover.
    // Role-match analog (event re-source + chain): the kept Blocklisting/Manga/MangaBlocklistService
    //   IHandle<ChapterDownloadFailedEvent> → MangaBlocklistAddedEvent → Download/Manga/
    //   AutoRetryOrchestrator chain. This service does NOT re-wire that chain — it publishes the
    //   ChapterDownloadFailedEvent the chain already subscribes to.
    //
    // ============================================================================
    // LANDMINE #1: ChapterDownloadFailedEvent.RowId is ChapterDownloadState.Id — an in-process-only
    // legacy field that is ABSENT on the generalized (matched TrackedDownload) path. We re-source
    // MangaId/ChapterId from the in-memory TrackedDownload.RemoteChapter (authoritative) and pass
    // RowId = 0. Never read ChapterDownloadState.Id here.
    //
    // GRABBED-HISTORY GATE (RESTORED — quick-task 260607-tjn / Fix A; see
    // .planning/debug/chapter-status-stale-failed.md): Check NOW pre-filters on grabbed history,
    // exactly like Sonarr FailedDownloadService.Check/ProcessFailed (GetGrabbedHistory(downloadId) +
    // grabbedItems.Empty() bail in BOTH methods). Blocklist + downloadFailed history + auto-retry fire
    // ONLY when the download correlates to an outstanding Grabbed ChapterHistory row for that exact
    // DownloadId AND that grab is not already reconciled to an Imported event (IsAlreadyImported —
    // history-based, the Sonarr TrackedDownloadAlreadyImported.IsImported peer, NOT the file-based
    // chapters.All(GetFilesByChapter) check). A stale/re-listed gateway "failed" job for an
    // already-imported or never-grabbed chapter therefore can NOT write a spurious downloadFailed row.
    // This RESTORES Sonarr parity (reduces divergence) — no DIVERGENCE.md entry.
    //
    // When the gate passes (grabbed + not-imported genuine failure), ProcessFailed publishes one
    // ChapterDownloadFailedEvent per chapter in the pack (WR-07). MangaBlocklistService.Handle inserts
    // the blocklist row (Insert FIRST) then publishes MangaBlocklistAddedEvent — AutoRetryOrchestrator
    // subscribes to THAT (anti-race contract; NOT ChapterDownloadFailedEvent directly). The D-03
    // AutoRedownloadFailed gate lives in AutoRetryOrchestrator and suppresses ONLY the re-search; the
    // blocklist insert is untouched, so for a genuine grabbed failure blocklisting happens whether or
    // not auto-redownload is enabled.
    //
    // The up-front terminal Failed transition in ProcessFailed is retained for the #301 one-shot
    // property (a row reused across polls via the monitor's registry merge is not re-processed).
    // ============================================================================
    public interface IMangaFailedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void ProcessFailed(TrackedDownload trackedDownload);
    }

    public class MangaFailedDownloadService : IMangaFailedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IChapterHistoryService _chapterHistoryService;
        private readonly Logger _logger;

        public MangaFailedDownloadService(
            IEventAggregator eventAggregator,
            IChapterHistoryService chapterHistoryService,
            Logger logger)
        {
            _eventAggregator = eventAggregator;
            _chapterHistoryService = chapterHistoryService;
            _logger = logger;
        }

        // Failed/Warning status → transition into FailedPending so the loop's ProcessFailed(...)
        // pass picks it up. Uses the kept TrackedDownload.Fail() (Error + FailedPending + CanBeRemoved).
        public void Check(TrackedDownload trackedDownload)
        {
            // Only a still-in-flight download transitions to FailedPending. A row already past
            // Downloading/ImportBlocked (e.g. a Failed instance reused across polls by the monitor's
            // registry merge, #301) must NOT be re-Failed. The monitor already gates the call site on
            // Downloading/ImportBlocked; this is the defensive in-method guard Sonarr
            // FailedDownloadService.Check also carries.
            if (trackedDownload.State != TrackedDownloadState.Downloading &&
                trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            var status = trackedDownload.DownloadItem?.Status;
            if (status == DownloadItemStatus.Failed || status == DownloadItemStatus.Warning)
            {
                // Sonarr FailedDownloadService.Check grabbed-history bail (RESTORED). Only a download
                // that correlates to an outstanding Grabbed ChapterHistory row for THIS exact DownloadId
                // is failed. A stale/re-listed gateway "failed" job for a never-grabbed (or fresh-jobId)
                // chapter has no matching grab → Warn + return WITHOUT calling Fail() (no FailedPending,
                // no downstream blocklist/history/auto-retry). TrackedDownload.Warn sets Status=Warning +
                // StatusMessages; it does NOT log via NLog, so no ExpectedWarns is needed in tests.
                var downloadId = trackedDownload.DownloadItem?.DownloadId;
                var grabbedItems = _chapterHistoryService.Find(downloadId, ChapterHistoryEventType.Grabbed);

                if (grabbedItems.Empty())
                {
                    trackedDownload.Warn("Download failed but wasn't grabbed by Mangarr, skipping automatic download handling");
                    return;
                }

                // Sonarr TrackedDownloadAlreadyImported.IsImported peer (history-based). A same-jobId
                // gateway re-list of an already-imported chapter still has a persisted Grabbed row, so
                // the grabbed gate alone would NOT bail — but its grab is reconciled to an Imported event,
                // so this skips it.
                if (IsAlreadyImported(trackedDownload))
                {
                    _logger.Debug("Failed download {0} is already imported (grab reconciled to Imported); skipping", downloadId);
                    return;
                }

                trackedDownload.Fail();
            }
        }

        public void ProcessFailed(TrackedDownload trackedDownload)
        {
            // Guard + terminal transition (#301). Only a FailedPending row is processed, and processing
            // moves it to the terminal Failed state so the next poll — which reuses this same instance
            // via the monitor's registry merge — does NOT re-publish (no duplicate ChapterDownloadFailed
            // events → no duplicate blocklist/history rows or repeated auto-retry searches). Mirrors
            // Sonarr FailedDownloadService.ProcessFailed (FailedPending guard + State = Failed).
            //
            // Set Failed up front (vs. Sonarr's just-before-publish placement): a row that bails on one
            // of the gates below must stay one-shot too (no per-poll re-entry), so transition before the
            // grabbed/already-imported bail rather than leaving it FailedPending forever.
            if (trackedDownload.State != TrackedDownloadState.FailedPending)
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.Failed;

            // Sonarr FailedDownloadService.ProcessFailed grabbed-history bail (RESTORED) — symmetric with
            // Check. Even though Check already pre-filters, ProcessFailed re-asserts the gate (Sonarr does
            // too) so a row that reached FailedPending by another path (or whose grab was reconciled to
            // Imported between Check and ProcessFailed) does not blocklist/auto-retry a non-failure.
            var downloadId = trackedDownload.DownloadItem?.DownloadId;
            var grabbedItems = _chapterHistoryService.Find(downloadId, ChapterHistoryEventType.Grabbed);

            if (grabbedItems.Empty())
            {
                _logger.Warn("Failed download {0} wasn't grabbed by Mangarr; skipping blocklist/auto-retry", downloadId);
                return;
            }

            if (IsAlreadyImported(trackedDownload))
            {
                _logger.Debug("Failed download {0} already imported; skipping blocklist/auto-retry", downloadId);
                return;
            }

            var remoteChapter = trackedDownload.RemoteChapter;
            if (remoteChapter?.Manga == null || remoteChapter.Chapters == null || !remoteChapter.Chapters.Any())
            {
                _logger.Warn(
                    "Failed download {0} has no resolved RemoteChapter; cannot blocklist/re-search",
                    trackedDownload.DownloadItem?.DownloadId);
                return;
            }

            var release = remoteChapter.Release;

            // Pitfall 2: a multi-chapter pack (c179/c180/c181) must blocklist + auto-retry EVERY
            // chapter, never collapse to the first. ChapterDownloadFailedEvent / MangaBlocklistService
            // are single-chapter-keyed (one blocklist row per ChapterId), so we emit one event per
            // chapter — each drives its own blocklist insert + MangaBlocklistAddedEvent → auto-retry.
            // The grab/import history paths already resolve ALL chapter ids; the failure path now
            // matches (WR-07 — previously only Chapters.First() was failed, silently dropping the
            // rest of the pack).
            foreach (var chapter in remoteChapter.Chapters)
            {
                // Landmine #1: re-source ids from the in-memory RemoteChapter; RowId = 0 (the in-process
                // ChapterDownloadState.Id does not exist on the generalized path).
                var failedEvent = new ChapterDownloadFailedEvent(
                    rowId: 0,
                    mangaId: remoteChapter.Manga.Id,
                    chapterId: chapter.Id,
                    failureReason: trackedDownload.DownloadItem?.Message ?? "Download failed")
                {
                    // Provenance Data keys from the in-memory RemoteChapter — the kept
                    // MangaBlocklistService.Handle reads Release / Source / SourceTitle off these to
                    // build the D-11 release-identity triple.
                    Release = release,
                    Source = trackedDownload.DownloadItem?.DownloadClientInfo?.Name,
                    DownloadClient = trackedDownload.DownloadItem?.DownloadClientInfo?.Type,
                    SourceTitle = release?.Title ?? trackedDownload.DownloadItem?.Title
                };

                // Grabbed + not-already-imported genuine failure — publishing the event drives
                // MangaBlocklistService.Handle (Insert FIRST, then MangaBlocklistAddedEvent →
                // AutoRetryOrchestrator). The D-03 re-search gate is downstream in AutoRetryOrchestrator;
                // the blocklist insert for a genuine grabbed failure is unconditional.
                _eventAggregator.PublishEvent(failedEvent);
            }
        }

        // Sonarr TrackedDownloadAlreadyImported.IsImported peer — history-based (NOT the file-based
        // chapters.All(GetFilesByChapter) check in MangaCompletedDownloadService.Import, a manga
        // invention the user explicitly rejected). A download is "already imported" when EVERY chapter
        // in the pack has its most-recent ChapterHistory event for THIS DownloadId == Imported (the grab
        // reconciled to an import). Robust against a same-jobId gateway re-list of an already-imported
        // chapter (whose persisted Grabbed row would otherwise pass the grabbed gate).
        private bool IsAlreadyImported(TrackedDownload trackedDownload)
        {
            var chapters = trackedDownload.RemoteChapter?.Chapters;
            if (chapters == null || !chapters.Any())
            {
                return false;
            }

            var historyItems = _chapterHistoryService.FindByDownloadId(trackedDownload.DownloadItem?.DownloadId);
            if (historyItems.Empty())
            {
                return false;
            }

            return chapters.All(c => historyItems.Where(h => h.ChapterId == c.Id)
                .OrderByDescending(h => h.Date)
                .FirstOrDefault() is { EventType: ChapterHistoryEventType.Imported });
        }
    }
}
