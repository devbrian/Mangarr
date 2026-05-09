namespace Mangarr.Api.V5.Manga.Subresources
{
    // Sonarr divergence: NEW shared sub-resource POCO per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    //
    // Minimal Chapter summary (Id + ChapterNumber + Title) used as a nested
    // subresource on richer resource shapes — `ChapterHistoryResource.Chapter`, etc.
    //
    // Phase 16 STRUCT-01 + STRUCT-08: TranslatedLanguage dropped — canonical Chapter is now
    // language-free at the (MangaId, ChapterNumber) grain; per-language data lives on
    // ChapterRelease and the API V5 wire-shape surfacing is deferred to Plan 16-05
    // (`ChapterResource.releases: [...]` collection).
    //
    // Phase 8 cleanup: collapse with `Episode` subresource pattern.
    public class ChapterSubresource
    {
        public int Id { get; set; }
        public int MangaId { get; set; }
        public decimal ChapterNumber { get; set; }
        public string? Title { get; set; }
    }
}
