namespace NzbDrone.Core.Parser.Manga.Model
{
    // Resolver output DTO. Mirrors Sonarr's Parser/Model/FindSeriesResult.cs shape —
    // pairs the resolved Manga aggregate with the match-type that produced it
    // (Title / Alias / Id) so callers (e.g. MangaParsingService.GetManga, downstream
    // RemoteChapter.MangaMatchType plumbing) can surface match-confidence to the
    // InteractiveSearch UI.
    //
    // Phase 8 audit: backfill for TV `FindSeriesResult` (no-sibling gap, audit
    // 08-09-09; full qualification of `NzbDrone.Core.Manga.Manga` per
    // Parser/Manga/CLAUDE.md type-vs-namespace collision rule until Phase 8 cutover
    // collapses `NzbDrone.Core.Parser.Manga` to `NzbDrone.Core.Parser`).
    public class FindMangaResult
    {
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public MangaMatchType MatchType { get; set; }

        public FindMangaResult(NzbDrone.Core.Manga.Manga manga, MangaMatchType matchType)
        {
            Manga = manga;
            MatchType = matchType;
        }
    }

    public enum MangaMatchType
    {
        Unknown = 0,
        Title = 1,
        Alias = 2,
        Id = 3
    }
}
