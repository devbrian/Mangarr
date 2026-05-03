using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        public static NamingConfig Default => new NamingConfig
        {
            RenameEpisodes = false,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            CustomColonReplacementFormat = string.Empty,
            MultiEpisodeStyle = MultiEpisodeStyle.PrefixedRange,
            StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}",
            DailyEpisodeFormat = "{Series Title} - {Air-Date} - {Episode Title} {Quality Full}",
            AnimeEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}",
            SeriesFolderFormat = "{Series Title}",
            SeasonFolderFormat = "Season {season}",
            SpecialsFolderFormat = "Specials",

            // Phase 5 D-13 + D-16 — Komga preset defaults seeded on first run.
            // Locked by 05-RESEARCH.md Pattern 5 templates + 05-CONTEXT.md item 13.
            // Wave 3 (plan 05-06) wires the apply-preset dropdown that re-fills these.
            StandardChapterFormat = "{Manga.Title} - Chapter {Chapter.Number:000}",
            MangaFolderFormat = "{Manga.Title}",
            RenameChapters = false
        };

        public bool RenameEpisodes { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string CustomColonReplacementFormat { get; set; }
        public MultiEpisodeStyle MultiEpisodeStyle { get; set; }
        public string StandardEpisodeFormat { get; set; }
        public string DailyEpisodeFormat { get; set; }
        public string AnimeEpisodeFormat { get; set; }
        public string SeriesFolderFormat { get; set; }
        public string SeasonFolderFormat { get; set; }
        public string SpecialsFolderFormat { get; set; }

        // Phase 5 D-13 — manga columns on existing NamingConfig singleton.
        // Phase 8 cleanup: drop TV-shaped columns when Tv/ deletes (D-04 invariant: ONE singleton).
        public string StandardChapterFormat { get; set; }
        public string MangaFolderFormat { get; set; }
        public bool RenameChapters { get; set; }
    }
}
