using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Debouncer = NzbDrone.Common.TPL.Debouncer;

namespace Sonarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-09 (D-13-04 forward-prophylactic) — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Queue/QueueStatusController.cs:1-79.
    //
    // Preserves: RestControllerWithSignalR<,> base, debounced 5s broadcast (Debouncer pattern),
    //   IHandle<MangaQueueUpdatedEvent> + IHandle<MangaPendingReleasesUpdatedEvent> subscriber pattern,
    //   GetQueueStatus Pause/Resume guard around the read so the response itself does not trigger
    //   another broadcast (mirrors QueueStatusController.cs:42-58 verbatim).
    //
    // Diverges from QueueStatusController:
    //   * Route literal "manga/queue/status" (NOT "queue/status") so Phase 13's frontend
    //     useQueueStatus.ts:15 can be re-pointed at the manga peer in a follow-up plan without
    //     colliding with the still-shipping TV path (D-13-16: phase is purely additive — TV
    //     QueueStatusController stays in place; Phase 15 deletes).
    //   * QueueStatusResource → MangaQueueStatusResource (counter shape preserved field-for-field).
    //   * QueueUpdatedEvent → MangaQueueUpdatedEvent (manga peer per MangaQueueController.cs:35;
    //     event class verified in src/NzbDrone.Core/Queue/Manga/MangaQueueUpdatedEvent.cs:13).
    //   * PendingReleasesUpdatedEvent → MangaPendingReleasesUpdatedEvent (manga peer at
    //     src/NzbDrone.Core/Queue/Manga/MangaQueueUpdatedEvent.cs:24 — reserved for the Plan 06-08
    //     auto-retry orchestrator hand-off; subscribing here keeps the SignalR consumer in sync
    //     when a pending release is held / promoted).
    //   * IQueueService → IMangaQueueService.GetMangaQueue() (defensive-copy projection per
    //     src/NzbDrone.Core/Queue/Manga/MangaQueueService.cs:54-60).
    //   * IPendingReleaseService → IMangaPendingReleaseService.GetPendingQueue() (manga peer per
    //     src/NzbDrone.Core/Download/Pending/Manga/IMangaPendingReleaseService.cs:28).
    //   * Series-discriminator (`q.Series != null`) → manga-discriminator (`q.MangaId.HasValue && q.MangaId > 0`).
    //     MangaQueueItem.MangaId is `int?` (src/NzbDrone.Core/Queue/Manga/MangaQueueItem.cs:31), NOT
    //     a navigation property like TV's Queue.Series — the projection POCO carries the FK directly.
    //   * TrackedDownloadStatus is exposed as a string on MangaQueueItem (line 46), not the
    //     TrackedDownloadStatus enum — compare against the enum name strings ("Error" / "Warning")
    //     produced by `td.Status.ToString()` in MangaQueueService.MapQueueItem (line 149).
    //
    // GetResourceById override mirrors QueueStatusController's NotImplementedException stub
    // (TV peer's static-list projection is push-only via Sync/Updated broadcasts — there is no
    // by-id read path; clients call the bare HttpGet to refresh).
    //
    // Phase 15 collapse: TV peer deletion + namespace rename collapses this to QueueStatusController.
    [V5ApiController("manga/queue/status")]
    public class MangaQueueStatusController : RestControllerWithSignalR<MangaQueueStatusResource, MangaQueueItem>,
                               IHandle<MangaQueueUpdatedEvent>, IHandle<MangaPendingReleasesUpdatedEvent>
    {
        private readonly IMangaQueueService _queueService;
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly Debouncer _broadcastDebounce;

        public MangaQueueStatusController(IBroadcastSignalRMessage broadcastSignalRMessage,
                                          IMangaQueueService queueService,
                                          IMangaPendingReleaseService pendingReleaseService)
            : base(broadcastSignalRMessage)
        {
            _queueService = queueService;
            _pendingReleaseService = pendingReleaseService;

            _broadcastDebounce = new Debouncer(BroadcastChange, TimeSpan.FromSeconds(5));
        }

        [NonAction]
        public override Results<Ok<MangaQueueStatusResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        [HttpGet]
        [Produces("application/json")]
        public MangaQueueStatusResource GetQueueStatus()
        {
            _broadcastDebounce.Pause();

            var queue = _queueService.GetMangaQueue();
            var pending = _pendingReleaseService.GetPendingQueue();

            var resource = new MangaQueueStatusResource
            {
                TotalCount = queue.Count + pending.Count,
                Count = queue.Count(q => q.MangaId.HasValue && q.MangaId.Value > 0) + pending.Count,
                UnknownCount = queue.Count(q => !q.MangaId.HasValue || q.MangaId.Value <= 0),
                Errors = queue.Any(q => q.MangaId.HasValue && q.MangaId.Value > 0 && q.TrackedDownloadStatus == "Error"),
                Warnings = queue.Any(q => q.MangaId.HasValue && q.MangaId.Value > 0 && q.TrackedDownloadStatus == "Warning"),
                UnknownErrors = queue.Any(q => (!q.MangaId.HasValue || q.MangaId.Value <= 0) && q.TrackedDownloadStatus == "Error"),
                UnknownWarnings = queue.Any(q => (!q.MangaId.HasValue || q.MangaId.Value <= 0) && q.TrackedDownloadStatus == "Warning")
            };

            _broadcastDebounce.Resume();

            return resource;
        }

        private void BroadcastChange()
        {
            BroadcastResourceChange(ModelAction.Updated, GetQueueStatus());
        }

        [NonAction]
        public void Handle(MangaQueueUpdatedEvent message)
        {
            _broadcastDebounce.Execute();
        }

        [NonAction]
        public void Handle(MangaPendingReleasesUpdatedEvent message)
        {
            _broadcastDebounce.Execute();
        }
    }
}
