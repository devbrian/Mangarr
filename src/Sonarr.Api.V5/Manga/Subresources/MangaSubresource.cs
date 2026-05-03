namespace Sonarr.Api.V5.Manga.Subresources
{
    // Sonarr divergence: NEW shared sub-resource POCO per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    //
    // Minimal Manga summary (Id + Title) used as a nested subresource on richer resource shapes
    // — `ChapterHistoryResource.Manga`, `MangaQueueResource.Manga`, `MangaBlocklistResource.Manga`
    // (when wired by the React layer in Phase 7). Avoids leaking the full `MangaResource` shape
    // through every controller payload (cheaper hydration + smaller wire size).
    //
    // Phase 8 cleanup: collapse with `Series` subresource pattern (see TV V5 controller usage of
    // `model.Series.ToResource()` style hydration which round-trips the full SeriesResource).
    public class MangaSubresource
    {
        public int Id { get; set; }
        public string? Title { get; set; }
    }
}
