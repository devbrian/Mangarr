namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 24 (v1.1 INSERTED 2026-05-17)
    // Option A — sentinel enum mirror for the ContentRatingSpecification FE dropdown
    // (PATTERNS line 815). No Sonarr/Tv peer; manga axis backed by MangaDex
    // contentRating field (4-value: safe/suggestive/erotica/pornographic). The Manga
    // entity stores ContentRating as a STRING (already-shipped per Phase 6); this enum
    // is FE-facing only for dropdown UX, and the spec's IsSatisfiedByWithoutNegate
    // helper maps the stored string -> enum at evaluation time. Explicit numeric values
    // start at 1 so accidental int defaults are non-meaningful.
    public enum MangaContentRating
    {
        Safe = 1,
        Suggestive = 2,
        Erotica = 3,
        Pornographic = 4
    }
}
