using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-08 (D-13-04
    // forward-prophylactic — sub-wave-B-addition backfill of the missing manga peer)
    // — see DIVERGENCE.md.
    //
    // Role-match analog: src/Sonarr.Api.V5/Queue/QueueDetailsController.cs (lines 14-77 —
    // RestControllerWithSignalR<QueueResource, NzbDrone.Core.Queue.Queue> + IHandle<QueueUpdatedEvent>
    // + IHandle<PendingReleasesUpdatedEvent>; queue+pending concat with optional series/episode filters
    // and subresource hydration via [FromQuery] QueueSubresource[]?).
    //
    // Trigger: 13-API-V5-SURFACE-FINDINGS.md §1.13 — `QueueDetailsController` exists at
    // `[V5ApiController("queue/details")]` but no manga peer exists. Frontend
    // `QueueDetailsProvider.tsx:31` currently fetches `/queue/details` (TV path); manga peer
    // needed for v1 regardless of current frontend use per D-13-04 forward-prophylactic.
    //
    // Manga sibling preserves (TV peer): RestControllerWithSignalR<TResource, TModel> base +
    // IHandle subscribers + queue+pending concat semantics + optional id-filter chain (mangaId
    // OR chapterIds) + optional subresource hydration via [FromQuery] enum-array + push-only
    // GetResourceById override (NotImplementedException — queue is push-only via Sync).
    //
    // Manga sibling diverges from QueueDetailsController:
    //   * Subscribes to MangaQueueUpdatedEvent + MangaPendingReleasesUpdatedEvent (Phase 6 D-20
    //     manga-side events) — NOT QueueUpdatedEvent / PendingReleasesUpdatedEvent. Both events
    //     verified extant at src/NzbDrone.Core/Queue/Manga/MangaQueueUpdatedEvent.cs:13-26.
    //   * Inject IMangaQueueService (Phase 6 D-20 — src/NzbDrone.Core/Queue/Manga/IMangaQueueService.cs:11-17)
    //     + IMangaPendingReleaseService (Phase 9 D-09-06 —
    //     src/NzbDrone.Core/Download/Pending/Manga/IMangaPendingReleaseService.cs:20-35).
    //   * Domain noun substitution: seriesId → mangaId, episodeIds → chapterIds.
    //   * SignalR resource name = `manga/queue/details` (auto-derives from
    //     [V5ApiController("manga/queue/details")] route attribute via
    //     RestControllerWithSignalR.cs:23-33). Coexists separately from `manga/queue` resource
    //     used by MangaQueueController.cs — TV peer pair (`queue` + `queue/details`) follows
    //     the same dual-resource pattern per RESEARCH SignalRListener Inventory line 262.
    //   * MangaQueueResource shape gates subresource hydration AFTER mapping (the existing
    //     single-arg ToResource extension at MangaQueueResource.cs:52 always populates Manga +
    //     Chapter); we apply the include-flags here by null-projecting subresources when the
    //     flag is false. TV gates BEFORE mapping via the two-arg ToResource extension; manga
    //     gates after to share the existing mapper with MangaQueueController.
    //
    // SignalR emission contract (mirrors TV QueueDetailsController shape verbatim):
    //   * `manga/queue/details` resource name auto-derives from [V5ApiController("manga/queue/details")]
    //     via RestControllerWithSignalR.cs:23-33 (apiAttribute.Resource read).
    //   * IHandle<MangaQueueUpdatedEvent> → BroadcastResourceChange(ModelAction.Sync) — no-id
    //     overload at RestControllerWithSignalR.cs:93-114 builds a SignalRMessage with
    //     Name = "manga/queue/details" + Action = ModelAction.Sync; client re-fetches the full
    //     queue.
    //   * IHandle<MangaPendingReleasesUpdatedEvent> → same Sync broadcast — pending-release
    //     mutations refresh the queue projection (Phase 6 D-20 reservation + Plan 06-08 auto-retry
    //     orchestrator hand-off path; matches TV peer's dual-IHandle subscription shape).
    //
    // Push-only contract (mirrors TV peer): GetResourceById override throws
    // NotImplementedException because the queue is push-only via Sync — there is no `GET /api/v5/manga/queue/details/{id}`
    // semantic; the [NonAction] override on GetResourceByIdWithErrorHandler suppresses the
    // base class's id-route from MVC discovery.
    //
    // Phase 8/15 cleanup: collapse with TV's QueueDetailsController (rename + flatten to
    // src/Sonarr.Api.V5/Queue/QueueDetailsController.cs post-Tv-namespace-delete) when Tv/ deletes.
    [V5ApiController("manga/queue/details")]
    public class MangaQueueDetailsController : RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>,
                               IHandle<MangaQueueUpdatedEvent>, IHandle<MangaPendingReleasesUpdatedEvent>
    {
        private readonly IMangaQueueService _queueService;
        private readonly IMangaPendingReleaseService _pendingReleaseService;

        public MangaQueueDetailsController(IBroadcastSignalRMessage broadcastSignalRMessage,
                                           IMangaQueueService queueService,
                                           IMangaPendingReleaseService pendingReleaseService)
            : base(broadcastSignalRMessage)
        {
            _queueService = queueService;
            _pendingReleaseService = pendingReleaseService;
        }

        [NonAction]
        public override Results<Ok<MangaQueueResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        protected override MangaQueueResource GetResourceById(int id)
        {
            // Push-only via ModelAction.Sync (mirrors TV QueueDetailsController.GetResourceById
            // at QueueDetailsController.cs:35-38). The IHandle subscribers fire the no-id
            // BroadcastResourceChange overload, which never round-trips through GetResourceById.
            throw new NotImplementedException();
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<List<MangaQueueResource>> GetQueue(int? mangaId,
                                                    [FromQuery] List<int> chapterIds,
                                                    [FromQuery] MangaQueueSubresource[]? includeSubresources = null)
        {
            var queue = _queueService.GetMangaQueue();
            var pending = _pendingReleaseService.GetPendingQueue();
            var fullQueue = queue.Concat(pending);

            var includeManga = includeSubresources?.Contains(MangaQueueSubresource.Manga) ?? false;
            var includeChapters = includeSubresources?.Contains(MangaQueueSubresource.Chapters) ?? false;

            IEnumerable<MangaQueueItem> filtered;

            if (mangaId.HasValue)
            {
                filtered = fullQueue.Where(q => q.MangaId == mangaId);
            }
            else if (chapterIds.Any())
            {
                // Mirrors TV QueueDetailsController episodeIds filter at QueueDetailsController.cs:55-60:
                // include any queue row whose Chapters list intersects the supplied chapterIds.
                // MangaQueueItem.Chapters is initialised to an empty List per
                // src/NzbDrone.Core/Queue/Manga/MangaQueueItem.cs:35 — Any() is safe.
                filtered = fullQueue.Where(q => q.Chapters.Any() &&
                                                chapterIds.IntersectBy(e => e, q.Chapters, c => c.Id, null).Any());
            }
            else
            {
                filtered = fullQueue;
            }

            return TypedResults.Ok(filtered.Select(m => Project(m, includeManga, includeChapters)).ToList());
        }

        // Manga-side gates AFTER mapping because the single-arg MangaQueueResourceMapper.ToResource
        // (MangaQueueResource.cs:52-99) is shared with MangaQueueController — extending it with
        // include-flags would force a mapper signature change rippling through that controller.
        // The null-projection gate here mirrors TV's two-arg gating semantically (subresources
        // only appear in the wire payload when the include-flag is true).
        private static MangaQueueResource Project(MangaQueueItem model, bool includeManga, bool includeChapters)
        {
            var resource = model.ToResource()!;

            if (!includeManga)
            {
                resource.Manga = null;
            }

            if (!includeChapters)
            {
                // The Chapter (single) + ChapterIds (collection) fields together model the
                // chapter subresource in MangaQueueResource (MangaQueueResource.cs:25-26 + 47).
                // Gate both consistently — the subresource enum value is the single load-bearing
                // toggle for the chapter projection on the wire.
                resource.Chapter = null;
                resource.ChapterIds = null;
            }

            return resource;
        }

        [NonAction]
        public void Handle(MangaQueueUpdatedEvent message)
        {
            BroadcastResourceChange(ModelAction.Sync);
        }

        [NonAction]
        public void Handle(MangaPendingReleasesUpdatedEvent message)
        {
            BroadcastResourceChange(ModelAction.Sync);
        }
    }
}
