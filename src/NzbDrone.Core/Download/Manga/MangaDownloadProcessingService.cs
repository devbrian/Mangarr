using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-04 / D-04) — see DIVERGENCE.md.
    // Provenance (control-flow): v5-develop:src/NzbDrone.Core/Download/DownloadProcessingService.cs
    //   (IExecute<ProcessMonitoredDownloadsCommand>: for each ImportPending → Import; for each
    //   FailedPending → ProcessFailed; then RemoveCompletedDownloads → DownloadCanBeRemovedEvent).
    // Role-match analog (the IExecute half): the in-process ProcessMangaCompletedDownloads poller
    //   (Execute → loop → process shape) — retired in Phase 39 RETIRE-01; this service is the
    //   generalized successor.
    //
    // ============================================================================
    // IExecute<ProcessMonitoredMangaDownloadsCommand> — pushed at the tail of every
    // MangaDownloadMonitoringService.Refresh() (queued-only; NO defaultTasks row):
    //
    //   (1) read the registry the monitor just built (IMangaDownloadMonitoringService
    //       .GetTrackedDownloads() — manga has no in-memory ITrackedDownloadService cache; the
    //       monitor that builds the list owns it)
    //   (2) for each State == ImportPending → _completedDownloadService.Import(td)
    //       (Plan 04; transitions the row through Importing inside the import core)
    //   (3) for each State == FailedPending → _failedDownloadService.ProcessFailed(td)
    //       (Plan 04; blocklist always + RowId=0)
    //   (4) RemoveCompletedDownloads — for each imported, removable row publish
    //       DownloadCanBeRemovedEvent(td)
    //
    //   D-04 — PRESERVE THE TRANSIENT Importing ROW. Eviction happens ONLY for rows that have
    //   reached State == Imported AND DownloadItem.CanBeRemoved. A Completed-but-not-yet-imported
    //   row is NEVER removed; the row must pass through Importing first. We do NOT evict on completion.
    //
    //   EVICTION = RemoveItem(deleteData:true) on the OWNING client — handled here via
    //   IHandle<DownloadCanBeRemovedEvent> (the KEPT eviction event). RemoveItem already deletes the
    //   in-process client's state row + scratch dir (InProcessImageDownloadClient.RemoveItem:99-122);
    //   the gateway path (Phase 38) issues DELETE /downloads/{id}. We NEVER hand-roll a manual
    //   _diskProvider.DeleteFolder — that legacy in-process scratch delete lived only in the
    //   retired ProcessMangaCompletedDownloads poller (Phase 39 RETIRE-01) and is NOT used on the
    //   generalized path.
    //
    // Phase 38 cleanup: becomes the canonical download-processing entry when the gateway path lands.
    // ============================================================================
    public interface IMangaDownloadProcessingService
    {
        void Process();
    }

    public class MangaDownloadProcessingService :
        IMangaDownloadProcessingService,
        IExecute<ProcessMonitoredMangaDownloadsCommand>,
        IHandle<DownloadCanBeRemovedEvent>
    {
        private readonly IMangaDownloadMonitoringService _monitoringService;
        private readonly IMangaCompletedDownloadService _completedDownloadService;
        private readonly IMangaFailedDownloadService _failedDownloadService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaDownloadProcessingService(
            IMangaDownloadMonitoringService monitoringService,
            IMangaCompletedDownloadService completedDownloadService,
            IMangaFailedDownloadService failedDownloadService,
            IDownloadClientFactory downloadClientFactory,
            IEventAggregator eventAggregator,
            Logger logger)
        {
            _monitoringService = monitoringService;
            _completedDownloadService = completedDownloadService;
            _failedDownloadService = failedDownloadService;
            _downloadClientFactory = downloadClientFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(ProcessMonitoredMangaDownloadsCommand message)
        {
            Process();
        }

        public void Process()
        {
            var trackedDownloads = _monitoringService.GetTrackedDownloads()
                .Where(t => t.IsTrackable)
                .ToList();

            foreach (var trackedDownload in trackedDownloads)
            {
                try
                {
                    // Process completed items first, then failed — a failed import can flip the row's
                    // state and be processed immediately rather than on the next cycle (mirrors v5).
                    if (trackedDownload.State == TrackedDownloadState.ImportPending)
                    {
                        // Import transitions the row through Importing (D-04) inside the import core,
                        // then reports whether it ACTUALLY imported. Only flip to Imported on a genuine
                        // success — a rejected/short-circuited import (null RemoteChapter, missing
                        // staging path, unresolved manga/chapter, rejected decision) leaves the row in
                        // ImportPending so RemoveCompletedDownloads does NOT evict it with
                        // deleteData:true (WR-05 — preserves scratch data for retry per the Q-8
                        // rejection-leaves-the-row-in-place posture).
                        trackedDownload.State = TrackedDownloadState.Importing;
                        var imported = _completedDownloadService.Import(trackedDownload);
                        trackedDownload.State = imported
                            ? TrackedDownloadState.Imported
                            : TrackedDownloadState.ImportPending;
                    }

                    if (trackedDownload.State == TrackedDownloadState.FailedPending)
                    {
                        _failedDownloadService.ProcessFailed(trackedDownload);
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to process download: {0}", trackedDownload.DownloadItem?.Title);

                    // #319: an exception thrown mid-Import leaves the row stranded in the transient
                    // Importing state (the state-revert line above never ran). The monitor only ever
                    // re-drives ImportPending rows, so a stranded Importing row is never retried AND —
                    // because nothing called trackedDownload.Warn — shows no error on the queue. This
                    // is exactly the silent "Downloaded - Importing" wedge from #318.
                    //
                    // Scope the surfaced message to the failing operation: a throw from the import path
                    // leaves State == Importing — revert it to ImportPending so the next poll re-drives,
                    // and label it "Import failed". A throw from ProcessFailed (the FailedPending branch)
                    // is NOT an import failure, so leave its state alone and use a generic label.
                    if (trackedDownload.State == TrackedDownloadState.Importing)
                    {
                        trackedDownload.State = TrackedDownloadState.ImportPending;
                        trackedDownload.Warn("Import failed: {0}", e.Message);
                    }
                    else
                    {
                        trackedDownload.Warn("Processing failed: {0}", e.Message);
                    }
                }
            }

            // Imported downloads are no longer trackable — evict them AFTER processing the trackable
            // set (D-04: only Imported + removable rows; never on completion).
            RemoveCompletedDownloads();
        }

        private void RemoveCompletedDownloads()
        {
            var removable = _monitoringService.GetTrackedDownloads()
                .Where(t => t.DownloadItem != null
                            && t.DownloadItem.CanBeRemoved
                            && t.State == TrackedDownloadState.Imported)
                .ToList();

            foreach (var trackedDownload in removable)
            {
                _eventAggregator.PublishEvent(new DownloadCanBeRemovedEvent(trackedDownload));
            }
        }

        // The KEPT eviction event → RemoveItem(deleteData:true) on the owning client. The in-process
        // client deletes its state row + scratch dir; never a manual _diskProvider.DeleteFolder here.
        public void Handle(DownloadCanBeRemovedEvent message)
        {
            var trackedDownload = message.TrackedDownload;
            var item = trackedDownload?.DownloadItem;
            if (item == null)
            {
                return;
            }

            var downloadClient = _downloadClientFactory.GetAvailableProviders()
                .FirstOrDefault(c => c.Definition.Id == trackedDownload.DownloadClient);

            if (downloadClient == null)
            {
                _logger.Warn(
                    "Cannot evict download {0} — owning client {1} is not available",
                    item.DownloadId,
                    trackedDownload.DownloadClient);
                return;
            }

            downloadClient.RemoveItem(item, deleteData: true);
        }
    }
}
