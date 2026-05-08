namespace Mangarr.Api.V5.Manga.Subresources
{
    // Sonarr divergence: NEW shared sub-resource POCO per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    //
    // Minimal Chapter summary (Id + ChapterNumber + Title + TranslatedLanguage) used as a nested
    // subresource on richer resource shapes — `ChapterHistoryResource.Chapter`, etc.
    //
    // Phase 8 cleanup: collapse with `Episode` subresource pattern.
    public class ChapterSubresource
    {
        public int Id { get; set; }
        public int MangaId { get; set; }
        public decimal ChapterNumber { get; set; }
        public string? Title { get; set; }
        public string? TranslatedLanguage { get; set; }
    }
}
