// Sonarr divergence: Phase 15 Plan 15-10 — V5/Queue/ DELETED; QueueBulkResource relocated to V5/Manga/Queue/.
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueController.cs (lines 25-408).
    //
    // PIPELINE-03: paged queue projection + SignalR push on every queue mutation.
    //
    // Manga sibling preserves: RestControllerWithSignalR<TResource, TModel> + IHandle<...>
    // SignalR push shape; BroadcastResourceChange(ModelAction.Sync) on every queue mutation.
    //
    // Manga sibling diverges from QueueController:
    //   * Subscribe to MangaQueueUpdatedEvent (Plan 06-05) instead of QueueUpdatedEvent.
    //   * Inject IMangaQueueService instead of the multi-service TV pipeline (no pending
    //     release service yet, no failed-download / ignored-download / blocklist plumbing
    //     beyond the simpler manga lifecycle).
    //   * SignalR resource name = "mangaqueue" via [V5ApiController("manga/queue")] route +
    //     RestControllerWithSignalR's Resource property derived from the route attribute.
    //   * GET returns the projected list (no DB paging because the queue is a static-list
    //     projection from TrackedDownloadRefreshedEvent — typically <100 in-flight rows).
    //
    // Phase 8 cleanup: collapse with QueueController when Tv/ deletes.
    [V5ApiController("manga/queue")]
    public class MangaQueueController : RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>,
                                        IHandle<MangaQueueUpdatedEvent>
    {
        private readonly IMangaQueueService _queueService;

        public MangaQueueController(IBroadcastSignalRMessage broadcastSignalRMessage,
                                    IMangaQueueService queueService)
            : base(broadcastSignalRMessage)
        {
            _queueService = queueService;
        }

        [NonAction]
        public override Results<Ok<MangaQueueResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        protected override MangaQueueResource? GetResourceById(int id)
        {
            var item = _queueService.Find(id);
            if (item == null)
            {
                throw new NotFoundException();
            }

            return item.ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<List<MangaQueueResource>> GetQueue()
        {
            // Static-list projection (Plan 06-05 — populated by IHandle<TrackedDownloadRefreshedEvent>).
            // Defensive copy already returned by GetMangaQueue() at the service layer.
            var items = _queueService.GetMangaQueue()
                .Select(q => q.ToResource()!)
                .ToList();
            return TypedResults.Ok(items);
        }

        [RestDeleteById]
        public NoContent RemoveQueueItem(int id)
        {
            _queueService.Remove(id);
            return TypedResults.NoContent();
        }

        // Phase 13 Plan 13-12 — F-01 gap closure (smoke-test quick-260507-p13).
        // Mirrors TV peer src/Mangarr.Api.V5/Queue/QueueController.cs:97-136 RemoveMany shape
        // (bulk DELETE belongs on the CRUD controller, NOT on QueueActionController). Manga's
        // simplified Remove signature has no blocklist/skipRedownload/changeCategory v1 params
        // (per existing [RestDeleteById] precedent at line 75-80) — Phase 15 collapse will
        // unify the signature when Tv/ deletes alongside the manga pending/blocklist plumbing.
        //
        // Phase 13 Plan 13-13 CR-02 hardening:
        //   * [Consumes("application/json")] — pinned media-type aligns with sibling manga V5
        //     endpoints (MangaQueueActionController.Grab, ChapterFileController bulk DELETE)
        //     and surfaces the request shape correctly through OpenAPI v5 doc gen. Without it,
        //     OpenAPI may emit `*/*` accept-list (breaking typed-client codegen) and form-
        //     urlencoded posts may bind `resource` as null (NRE on resource.Ids enumeration).
        //   * Null-guard on resource / resource.Ids — a missing or null body short-circuits to
        //     NoContent rather than NRE-ing inside the foreach. The TV peer's wider param surface
        //     masks this; the manga simplification reintroduces the risk.
        //   * .Distinct() before iterating — duplicate ids in the request body would otherwise
        //     fire duplicate IMangaQueueService.Remove calls, each publishing a
        //     MangaQueueUpdatedEvent and triggering a redundant SignalR Sync broadcast (UI
        //     thrash). Mirrors TV QueueController.cs:122/127 DistinctBy semantics.
        [HttpDelete("bulk")]
        [Consumes("application/json")]
        public NoContent RemoveMany([FromBody] QueueBulkResource resource)
        {
            if (resource?.Ids == null)
            {
                return TypedResults.NoContent();
            }

            foreach (var id in resource.Ids.Distinct())
            {
                _queueService.Remove(id);
            }

            return TypedResults.NoContent();
        }

        [NonAction]
        public void Handle(MangaQueueUpdatedEvent message)
        {
            // SignalR fan-out — every queue projection refresh emits MangaQueueUpdatedEvent
            // (Plan 06-05); the React Activity panel (Phase 7) subscribes for animated diffs.
            // ModelAction.Sync triggers a broadcast without a specific record id, signaling the
            // client to re-fetch the full list (matches TV QueueController convention).
            BroadcastResourceChange(ModelAction.Sync);
        }
    }
}
