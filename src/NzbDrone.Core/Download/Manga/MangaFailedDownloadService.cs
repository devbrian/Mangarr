using System.Linq;
using NLog;
using NzbDrone.Core.Download.TrackedDownloads;
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
    // BLOCKLIST ALWAYS FIRES: ProcessFailed publishes ChapterDownloadFailedEvent unconditionally.
    // MangaBlocklistService.Handle(ChapterDownloadFailedEvent) inserts the blocklist row (Insert
    // FIRST) then publishes MangaBlocklistAddedEvent — AutoRetryOrchestrator subscribes to THAT
    // (anti-race contract; NOT ChapterDownloadFailedEvent directly). The new D-03 AutoRedownloadFailed
    // gate lives in AutoRetryOrchestrator and suppresses ONLY the re-search; the blocklist insert is
    // untouched, so blocklisting happens whether or not auto-redownload is enabled.
    //
    // Phase 38 cleanup: becomes the canonical failed-download entry when the gateway path lands.
    // ============================================================================
    public interface IMangaFailedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void ProcessFailed(TrackedDownload trackedDownload);
    }

    public class MangaFailedDownloadService : IMangaFailedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaFailedDownloadService(
            IEventAggregator eventAggregator,
            Logger logger)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // Failed/Warning status → transition into FailedPending so the loop's ProcessFailed(...)
        // pass picks it up. Uses the kept TrackedDownload.Fail() (Error + FailedPending + CanBeRemoved).
        public void Check(TrackedDownload trackedDownload)
        {
            var status = trackedDownload.DownloadItem?.Status;
            if (status == DownloadItemStatus.Failed || status == DownloadItemStatus.Warning)
            {
                trackedDownload.Fail();
            }
        }

        public void ProcessFailed(TrackedDownload trackedDownload)
        {
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

                // BLOCKLIST ALWAYS — publishing the event drives MangaBlocklistService.Handle (Insert
                // FIRST, then MangaBlocklistAddedEvent → AutoRetryOrchestrator). The D-03 re-search gate
                // is downstream in AutoRetryOrchestrator; the blocklist insert is unconditional.
                _eventAggregator.PublishEvent(failedEvent);
            }
        }
    }
}
