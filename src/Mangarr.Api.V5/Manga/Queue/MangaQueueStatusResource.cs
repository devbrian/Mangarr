using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 resource per Phase 13 Plan 13-09 (D-13-04 forward-prophylactic) — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueStatusResource.cs:1-15.
    //
    // Counter DTO mirrored field-for-field from QueueStatusResource — counters are domain-neutral
    // (TotalCount / Count / UnknownCount / Errors / Warnings / UnknownErrors / UnknownWarnings).
    // No TV-shape fields like `Series` to translate; the manga discriminator lives in
    // MangaQueueStatusController.GetQueueStatus where `q.MangaId > 0` replaces the TV
    // `q.Series != null` check (Plan 13-09 controller).
    //
    // ResourceName auto-derives via RestResource.ResourceName as "mangaqueuestatus" (lowercase,
    // strips "resource" suffix). The actual SignalR fan-out name is set explicitly by
    // RestControllerWithSignalR via the [V5ApiController("manga/queue/status")] route attribute
    // (RestControllerWithSignalR.cs:23-33 — apiAttribute.Resource is preferred over the type-name
    // fallback when supplied).
    //
    // Phase 15 cleanup: collapse with QueueStatusResource when Tv/ deletes.
    public class MangaQueueStatusResource : RestResource
    {
        public int TotalCount { get; set; }
        public int Count { get; set; }
        public int UnknownCount { get; set; }
        public bool Errors { get; set; }
        public bool Warnings { get; set; }
        public bool UnknownErrors { get; set; }
        public bool UnknownWarnings { get; set; }
    }
}
