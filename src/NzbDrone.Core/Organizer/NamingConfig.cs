using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // TV-shaped properties (RenameEpisodes / MultiEpisodeStyle / Standard|Daily|AnimeEpisodeFormat /
    // SeriesFolderFormat / SeasonFolderFormat) stripped per D-22. Manga columns
    // (StandardChapterFormat / MangaFolderFormat / RenameChapters) preserved per Phase 5 D-13.
    // ColonReplacementFormat / CustomColonReplacementFormat / ReplaceIllegalCharacters /
    // SpecialsFolderFormat / RenameEpisodes columns remain in schema (Migration 001) — the
    // RenameEpisodes / SpecialsFolderFormat properties retained as ints/strings for schema
    // round-trip but unused by manga rename pipeline.
    public class NamingConfig : ModelBase
    {
        public static NamingConfig Default => new NamingConfig
        {
            RenameEpisodes = false,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = 0,
            CustomColonReplacementFormat = string.Empty,
            SpecialsFolderFormat = "Specials",

            // Phase 5 D-13 + D-16 — Komga preset defaults seeded on first run.
            // Locked by 05-RESEARCH.md Pattern 5 templates + 05-CONTEXT.md item 13.
            StandardChapterFormat = "{Manga.Title} - Chapter {Chapter.Number:000}",
            MangaFolderFormat = "{Manga.Title}",
            RenameChapters = false
        };

        // Schema-round-trip columns (Migration 001 baseline).
        public bool RenameEpisodes { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public int ColonReplacementFormat { get; set; }
        public string CustomColonReplacementFormat { get; set; }
        public string SpecialsFolderFormat { get; set; }

        // Phase 5 D-13 — manga columns on the NamingConfig singleton (D-04 invariant: ONE singleton).
        public string StandardChapterFormat { get; set; }
        public string MangaFolderFormat { get; set; }
        public bool RenameChapters { get; set; }
    }
}
