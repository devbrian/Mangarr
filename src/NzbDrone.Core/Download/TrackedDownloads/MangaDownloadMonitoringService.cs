using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-01 / LOOP-05) — see DIVERGENCE.md.
    // Provenance (control-flow / publish-site — there is NO live TV DownloadMonitoringService at
    // HEAD, deleted in the Phase 15 Tv/ cutover; this is the dead-queue root cause):
    //   v5-develop:src/NzbDrone.Core/Download/TrackedDownloads/DownloadMonitoringService.cs
    //   (Refresh() = DownloadHandlingEnabled() → GetItems() → TrackDownload → Failed/Completed Check →
    //    PublishEvent(TrackedDownloadRefreshedEvent) as the LAST step → Push(ProcessMonitoredDownloadsCommand);
    //    5s Debouncer on grab/import events). The publisher it feeds (MangaQueueService) has NO live
    //    reference publisher to diff against (Pitfall 1) — the v5-develop Refresh() tail is the cited
    //    publish site.
    // Surface conventions (hybrid IExecute + IHandle + IManageCommandQueue): the original role-match
    //   analog (the in-process ProcessMangaCompletedDownloads poller) was retired in Phase 39 RETIRE-01;
    //   this monitoring service is now the canonical hybrid surface.
    //
    // ============================================================================
    // THE POLL HEART (LOOP-01) + THE DEAD-QUEUE FIX (LOOP-05):
    //
    //   IExecute<RefreshMonitoredMangaDownloadsCommand>  — the 1-min scheduled poll (TaskManager
    //                                                      .defaultTasks; runtime registration,
    //                                                      anti-pattern C).
    //   IHandle<ChapterGrabbedEvent> / IHandle<ChapterImportedEvent>
    //                                                    — a 5s Debouncer wakes Refresh() shortly
    //                                                      after a grab/import so the queue reflects
    //                                                      the new in-flight/imported state without
    //                                                      waiting up to a minute (coalesces bursts).
    //   IHandle<MangaAddedEvent> / IHandle<MangaUpdatedEvent> / IHandle<MangaBulkEditedEvent> /
    //   IHandle<MangaDeletedEvent>                       — the edit-family cache reconcile (issue
    //                                                      #278; mirror of Sonarr
    //                                                      TrackedDownloadService.Handle(Series{Added,
    //                                                      Edited,BulkEdited,Deleted}Event)). The
    //                                                      single-item trigger is MangaUpdatedEvent,
    //                                                      NOT MangaEditedEvent: per the Phase-10
    //                                                      semantic split MangaEditedEvent fires ONLY
    //                                                      from the controller-PUT 3-arg UpdateManga
    //                                                      (which ALSO fires MangaUpdatedEvent), so
    //                                                      MangaUpdatedEvent strictly supersets it AND
    //                                                      additionally covers the 2-arg paths the
    //                                                      edit event misses — most importantly
    //                                                      MoveMangaService.RevertPath's rollback after
    //                                                      a failed root-folder move (else a row just
    //                                                      swapped to the failed destination path would
    //                                                      stay stale until poll-rebuild — CodeRabbit
    //                                                      PR #317), plus MangaLinksController and the
    //                                                      RefreshMangaService metadata pulse.
    //                                                      Subscribing to MangaUpdatedEvent instead of
    //                                                      BOTH also avoids a double reconcile/republish
    //                                                      on the controller-PUT path. When an
    //                                                      update touches a manga that a tracked-download
    //                                                      row references, swap the stale Manga snapshot
    //                                                      for the freshly-edited instance IN PLACE and
    //                                                      republish — so a settled row reused across
    //                                                      polls (#301) reflects the edit (e.g. a
    //                                                      root-folder bulk move's new Path) IMMEDIATELY
    //                                                      instead of waiting for the next poll-rebuild
    //                                                      (eventual where Sonarr is immediate). See
    //                                                      ReconcileTrackedManga — it does NOT rebuild
    //                                                      the row (no TrackDownload, no State reset),
    //                                                      so it is NOT the Pitfall-9 hazard (that bans
    //                                                      publishing from the grab/import handlers,
    //                                                      which WOULD need a registry rebuild).
    //
    //   Refresh() control flow (write/track FIRST, publish LAST — anti-pattern F):
    //     (0) pause the debounce + snapshot the prior registry keyed by DownloadId (the #301 merge)
    //     (1) for each DownloadHandlingEnabled() client → GetItems()
    //     (2)   for each item → reuse the prior instance if it has settled past Downloading AND
    //           ImportBlocked (preserve its State; #301), else TrackDownload(definition, item) (Plan 03
    //           matcher). ImportBlocked is Mangarr's unresolved shell — it is rebuilt so a later poll
    //           with available metadata can re-resolve it (Codex PR #304).
    //     (3)     run _failedDownloadService.Check + _completedDownloadService.Check (Plan 04) — only
    //             for a rebuilt Downloading/ImportBlocked row; a reused settled row is NOT re-Checked
    //     (4)   accumulate trackable downloads into a List<TrackedDownload>
    //     (5) cache the list as the registry MangaDownloadProcessingService reads
    //     (6) PUBLISH TrackedDownloadRefreshedEvent  ← THE LAST PUBLISH (the dead-queue fix; wakes
    //                                                   MangaQueueService → MangaQueueUpdatedEvent →
    //                                                   SignalR). DO NOT publish from a grab/import
    //                                                   handler (Pitfall 9 — flicker/empty queue).
    //     (7) Push(ProcessMonitoredMangaDownloadsCommand)  ← queued-only (NO defaultTasks row)
    //     (8) resume the debounce (finally)
    //
    //   DO NOT touch MangaQueueService (the KEPT IHandle<TrackedDownloadRefreshedEvent> consumer —
    //   it rebuilds its static projection and re-publishes MangaQueueUpdatedEvent → SignalR). Its
    //   Protocol == Http filter is now a harmless HERITAGE GUARD, not a live co-existence
    //   requirement: the TV QueueService it once disambiguated from was deleted in the Phase 15
    //   Tv/ cutover, so MangaQueueService is the sole TrackedDownloadRefreshedEvent subscriber
    //   (HEAD-verified Phase 36 Plan 02). See Queue/Manga/CLAUDE.md.
    //
    // Phase 38 cleanup: collapse with the gateway-path monitor; the publish-LAST tail is canonical.
    // ============================================================================
    public interface IMangaDownloadMonitoringService
    {
        // The registry of the most recent Refresh() — the rows the MangaDownloadProcessingService
        // imports / processes-failed / evicts. Manga has no in-memory ITrackedDownloadService cache
        // (the TV one was deleted in the Phase 15 Tv/ cutover); the monitor that builds the list owns it.
        List<TrackedDownload> GetTrackedDownloads();
    }

    public class MangaDownloadMonitoringService :
        IMangaDownloadMonitoringService,
        IExecute<RefreshMonitoredMangaDownloadsCommand>,
        IHandle<ChapterGrabbedEvent>,
        IHandle<ChapterImportedEvent>,
        IHandle<MangaAddedEvent>,
        IHandle<MangaUpdatedEvent>,
        IHandle<MangaBulkEditedEvent>,
        IHandle<MangaDeletedEvent>
    {
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IMangaTrackedDownloadService _trackedDownloadService;
        private readonly IMangaCompletedDownloadService _completedDownloadService;
        private readonly IMangaFailedDownloadService _failedDownloadService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;
        private readonly Debouncer _refreshDebounce;

        // Instance state (not static): convention-registered services use DryIoc's default
        // Reuse.Singleton (NzbDrone.Common/Composition/Extensions.cs), so the single monitor instance
        // is the one IMangaDownloadProcessingService injects — instance fields share correctly without
        // the cross-instance coupling (and test-isolation hazard) a static would carry. The registry
        // now MERGES across polls keyed by DownloadId (#301), so it must not bleed between instances.
        private readonly object _registryLock = new object();
        private List<TrackedDownload> _trackedDownloads = new List<TrackedDownload>();

        public MangaDownloadMonitoringService(
            IDownloadClientFactory downloadClientFactory,
            IMangaTrackedDownloadService trackedDownloadService,
            IMangaCompletedDownloadService completedDownloadService,
            IMangaFailedDownloadService failedDownloadService,
            IEventAggregator eventAggregator,
            IManageCommandQueue commandQueueManager,
            Logger logger)
        {
            _downloadClientFactory = downloadClientFactory;
            _trackedDownloadService = trackedDownloadService;
            _completedDownloadService = completedDownloadService;
            _failedDownloadService = failedDownloadService;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _logger = logger;

            // 5s debounce on grab/import — coalesce rapid bursts into a single Refresh().
            _refreshDebounce = new Debouncer(QueueRefresh, TimeSpan.FromSeconds(5));
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            lock (_registryLock)
            {
                // Defensive copy so a concurrent Refresh() assignment cannot tear an in-flight read.
                return _trackedDownloads.ToList();
            }
        }

        public void Execute(RefreshMonitoredMangaDownloadsCommand message)
        {
            Refresh();
        }

        public void Handle(ChapterGrabbedEvent message)
        {
            // Wake the debounced Refresh — NOT a direct Refresh (Pitfall 9: never publish the
            // TrackedDownloadRefreshedEvent from a grab handler; only Refresh()'s tail publishes).
            _refreshDebounce.Execute();
        }

        public void Handle(ChapterImportedEvent message)
        {
            _refreshDebounce.Execute();
        }

        // ── Edit-family cache reconcile (issue #278) ───────────────────────────────────────────────
        // Mirror of Sonarr TrackedDownloadService.Handle(Series{Added,Edited,BulkEdited,Deleted}Event).
        // Sonarr re-resolves each affected cached item via UpdateCachedItem (a DB re-parse); the manga
        // events carry the fresh Manga instance(s) in their payload, so the reconcile is a direct
        // reference-swap instead. Added/Updated/Deleted carry a single Manga; BulkEdited carries the
        // list (the root-folder bulk-move path that motivated #278).
        //
        // The single-item trigger is MangaUpdatedEvent (the "any non-bulk update" signal), NOT
        // MangaEditedEvent: MangaUpdatedEvent supersets the edit event (the controller-PUT 3-arg
        // UpdateManga fires both) and additionally fires on MoveMangaService.RevertPath's rollback
        // after a failed root-folder move + MangaLinksController + the RefreshMangaService pulse —
        // paths MangaEditedEvent misses (CodeRabbit PR #317).

        public void Handle(MangaAddedEvent message)
        {
            ReconcileTrackedManga(new[] { message.Manga });
        }

        public void Handle(MangaUpdatedEvent message)
        {
            ReconcileTrackedManga(new[] { message.Manga });
        }

        public void Handle(MangaBulkEditedEvent message)
        {
            ReconcileTrackedManga(message.Manga);
        }

        public void Handle(MangaDeletedEvent message)
        {
            ReconcileTrackedManga(new[] { message.Manga });
        }

        // Swap the stale RemoteChapter.Manga snapshot for the freshly-edited instance on every registry
        // row that references an affected manga, then republish the (reconciled) registry so the KEPT
        // MangaQueueService projection → MangaQueueUpdatedEvent → SignalR re-renders immediately.
        //
        // Reference-swap ONLY — this never calls TrackDownload / rebuilds the row, so a settled row's
        // terminal State (Failed/Imported) is preserved (rebuilding would reset it to Downloading and
        // re-run the Completed/Failed Checks — the #301 duplicate-event bug). Settled rows reused across
        // polls (#301) are exactly the rows that benefit: without this they would carry the pre-edit Path
        // until evicted. Downloading/ImportBlocked rows rebuild next poll regardless, but the republish
        // surfaces the edit now. Publishing here is SAFE (unlike the Pitfall-9 grab/import handlers): no
        // registry rebuild happens, so there is no flicker-to-empty window.
        private void ReconcileTrackedManga(IReadOnlyCollection<NzbDrone.Core.Manga.Manga> editedManga)
        {
            if (editedManga == null || editedManga.Count == 0)
            {
                return;
            }

            // Last-wins on a duplicate id (not expected — bulk edits are de-duped upstream).
            var byId = new Dictionary<int, NzbDrone.Core.Manga.Manga>();
            foreach (var manga in editedManga)
            {
                if (manga != null)
                {
                    byId[manga.Id] = manga;
                }
            }

            if (byId.Count == 0)
            {
                return;
            }

            var reconciled = false;
            lock (_registryLock)
            {
                foreach (var trackedDownload in _trackedDownloads)
                {
                    var manga = trackedDownload.RemoteChapter?.Manga;
                    if (manga != null && byId.TryGetValue(manga.Id, out var fresh))
                    {
                        trackedDownload.RemoteChapter.Manga = fresh;
                        reconciled = true;
                    }
                }
            }

            // Only republish when something actually matched (mirrors Sonarr's `if (cachedItems.Any())`
            // gate) — an edit to a manga with no in-flight/settled download is a no-op for the queue.
            if (reconciled)
            {
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(GetTrackedDownloads()));
            }
        }

        // The debounced action — push the scheduled poll command rather than calling Refresh()
        // directly, so the Refresh runs on the command-queue thread (mirrors v5 QueueRefresh).
        private void QueueRefresh()
        {
            _commandQueueManager.Push(new RefreshMonitoredMangaDownloadsCommand(), CommandPriority.High);
        }

        private void Refresh()
        {
            _refreshDebounce.Pause();
            try
            {
                // (0) Snapshot the prior registry keyed by DownloadId. A download already past the
                // Downloading state (Failed/Imported/…) keeps its existing instance + terminal State
                // across polls instead of being rebuilt as Downloading and re-Checked/re-processed
                // every cycle (#301; mirrors Sonarr TrackedDownloadService's DownloadId-keyed cache
                // reuse). Without this the in-process client's still-present Failed row re-Fails every
                // minute → duplicate ChapterDownloadFailedEvent → duplicate blocklist/history/auto-retry.
                Dictionary<string, TrackedDownload> previous;
                lock (_registryLock)
                {
                    previous = IndexByDownloadId(_trackedDownloads);
                }

                var trackedDownloads = new List<TrackedDownload>();

                foreach (var downloadClient in _downloadClientFactory.DownloadHandlingEnabled())
                {
                    trackedDownloads.AddRange(ProcessClientDownloads(downloadClient, previous));
                }

                // (5) Cache the registry BEFORE publishing — the ProcessMonitored command (pushed at
                // the tail) reads this list, and it must reflect THIS refresh.
                lock (_registryLock)
                {
                    _trackedDownloads = trackedDownloads;
                }

                // (6) THE LAST PUBLISH — the dead-queue fix. Wakes the KEPT MangaQueueService
                // projection → MangaQueueUpdatedEvent → SignalR. Must come AFTER tracking + Checks.
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));

                // (7) Queued-only — pushed at the tail of every Refresh() (NO defaultTasks row).
                _commandQueueManager.Push(new ProcessMonitoredMangaDownloadsCommand(), CommandPriority.High);
            }
            finally
            {
                _refreshDebounce.Resume();
            }
        }

        private List<TrackedDownload> ProcessClientDownloads(IDownloadClient downloadClient, IReadOnlyDictionary<string, TrackedDownload> previous)
        {
            var trackedDownloads = new List<TrackedDownload>();

            List<DownloadClientItem> items;
            try
            {
                items = downloadClient.GetItems().ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to retrieve items from download client {0}", downloadClient.Definition.Name);
                return trackedDownloads;
            }

            foreach (var item in items)
            {
                var trackedDownload = ProcessClientItem(downloadClient, item, previous);
                trackedDownloads.AddIfNotNull(trackedDownload);
            }

            return trackedDownloads;
        }

        private TrackedDownload ProcessClientItem(IDownloadClient downloadClient, DownloadClientItem item, IReadOnlyDictionary<string, TrackedDownload> previous)
        {
            TrackedDownload trackedDownload = null;
            try
            {
                // Registry merge (#301): reuse the prior-poll instance — preserving its State — once a
                // download has settled past the in-flight/unresolved phase, like Sonarr
                // TrackedDownloadService.TrackDownload's reuse branch (existing.State != Downloading).
                //
                // Mangarr ALSO excludes ImportBlocked from reuse. Unlike Sonarr (where an unresolved
                // download stays Downloading and is re-resolved every poll), Mangarr marks an
                // *unresolvable shell* ImportBlocked with RemoteChapter == null (MangaTrackedDownloadService
                // .BuildTrackedDownload). Such a shell MUST be rebuilt each poll so a later poll whose
                // download-history/title metadata has since resolved can recover it — otherwise it would
                // stay RemoteChapter == null forever and a subsequent completion would loop on a
                // null-remote import (Codex review on PR #304). Only the client-item snapshot is refreshed
                // on reuse; the instance is NOT rebuilt (which would reset State to Downloading).
                if (!string.IsNullOrWhiteSpace(item.DownloadId)
                    && previous.TryGetValue(item.DownloadId, out var existing)
                    && existing.State != TrackedDownloadState.Downloading
                    && existing.State != TrackedDownloadState.ImportBlocked)
                {
                    existing.DownloadItem = item;
                    existing.IsTrackable = true;
                    trackedDownload = existing;
                }
                else
                {
                    trackedDownload = _trackedDownloadService.TrackDownload(
                        (DownloadClientDefinition)downloadClient.Definition,
                        item);
                }

                if (trackedDownload is { State: TrackedDownloadState.Downloading or TrackedDownloadState.ImportBlocked })
                {
                    // Run the Failed Check first then the Completed Check — a failed-then-completed
                    // status flip is rare, but ordering matches v5 ProcessClientItem. A terminal row
                    // (Failed/Imported/Ignored) is NOT re-Checked (that was #301's duplicate-event bug);
                    // an ImportBlocked row IS still re-Checked so a now-failed/now-completed transition
                    // is detected (mirrors Sonarr ProcessClientItem's Downloading-or-ImportBlocked gate).
                    _failedDownloadService.Check(trackedDownload);
                    _completedDownloadService.Check(trackedDownload);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't process tracked download {0}", item.Title);
            }

            return trackedDownload;
        }

        // Index the registry by DownloadId for the cross-poll merge (#301). Last-wins on a duplicate id
        // (a download is owned by exactly one client, so collisions are not expected); rows without a
        // usable id are skipped — they cannot be merged and are simply rebuilt next poll.
        private static Dictionary<string, TrackedDownload> IndexByDownloadId(IEnumerable<TrackedDownload> trackedDownloads)
        {
            var index = new Dictionary<string, TrackedDownload>(StringComparer.OrdinalIgnoreCase);

            foreach (var trackedDownload in trackedDownloads)
            {
                var downloadId = trackedDownload.DownloadItem?.DownloadId;
                if (!string.IsNullOrWhiteSpace(downloadId))
                {
                    index[downloadId] = trackedDownload;
                }
            }

            return index;
        }
    }
}
