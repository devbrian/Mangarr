namespace Mangarr.Api.V5.Manga.Subresources
{
    // Sonarr divergence: NEW shared sub-resource POCO per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    //
    // Minimal Chapter summary (Id + ChapterNumber + Title) used as a nested
    // subresource on richer resource shapes — `ChapterHistoryResource.Chapter`, etc.
    //
    // Phase 16 STRUCT-01 + Phase 16.1: TranslatedLanguage dropped — canonical Chapter is
    // language-free at the (MangaId, ChapterNumber) grain. Per-translation data lives on
    // ChapterFile.TranslatedLanguage + ChapterFile.ScanlationGroup post-import (Phase 6
    // PIPELINE-04 + Phase 16.1 D-04 — Sonarr-canonical pattern; mirrors
    // EpisodeFile.Languages placement). The API V5 ChapterFileResource carries the
    // per-translation wire fields (camelCase `translatedLanguage` + `scanlationGroup`).
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
