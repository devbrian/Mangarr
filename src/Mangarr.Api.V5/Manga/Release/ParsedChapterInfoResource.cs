using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga sibling per debug session interactive-search-rejections (2026-05-08).
    // Role-match analog: src/Sonarr.Api.V5/Release/ParsedEpisodeInfoResource.cs (deleted by Phase
    // 15-10 commit d0b67fdf3 along with the rest of the V5 TV Release/ directory).
    //
    // Ports the canonical Sonarr V5 nested ParsedInfo wire shape so the frontend
    // useReleases.ts `ParsedInfo` interface (frontend/src/InteractiveSearch/useReleases.ts:62-75)
    // — which the row destructures off `release.parsedInfo.{quality, episodeNumbers, isDaily,
    // seasonNumber, ...}` — resolves to a populated object. Frontend FILTERS / SORT predicates also
    // read `item.parsedInfo.fullSeason` and `item.parsedInfo.quality.quality.id`.
    //
    // Manga divergences from upstream Sonarr ParsedEpisodeInfoResource:
    //   * `Quality` is a stub `{ quality: { id: 0, name: "Unknown" }, revision: { version: 1, real: 0, isRepack: false } }`
    //     — manga has no quality model per Phase 5 D-04. The frontend EpisodeQuality component is
    //     a Phase 15 stub that returns null (frontend/src/Episode/EpisodeQuality.tsx); the only
    //     consumer that READS the quality is the SORT predicate at useReleases.ts:280, which
    //     dereferences `item.parsedInfo.quality.quality.id` — sending the stub keeps the field
    //     non-null and the access non-throwing.
    //   * `SeasonNumber` always 0 (manga has no Season concept per PROJECT.md Out-of-Scope).
    //   * `EpisodeNumbers` carries chapter numbers cast to int (the frontend label is TV-flavored
    //     but the row only reads it for display via the stubbed ReleaseSceneIndicator). Decimals
    //     truncate; the row label remains correct for whole-chapter releases (which is the
    //     overwhelming majority — decimal-chapter sub-numbering is rare).
    //   * `AbsoluteEpisodeNumbers` carries AbsoluteChapterNumber when present, [] otherwise.
    //   * `IsDaily / IsAbsoluteNumbering / IsPossibleSpecialEpisode` always false; `Special` true
    //     iff `ChapterType != Regular`.
    //   * `SeriesTitle` = MangaTitle.
    //
    // Phase 8 cleanup: collapse with the unified ParsedEpisodeInfoResource when Tv/ deletes.
    public class ParsedChapterInfoResource
    {
        public StubQualityModel? Quality { get; set; }
        public string? ReleaseGroup { get; set; }
        public string? ReleaseHash { get; set; }
        public bool FullSeason { get; set; }
        public int SeasonNumber { get; set; }
        public string? AirDate { get; set; }
        public string? SeriesTitle { get; set; }
        public int[] EpisodeNumbers { get; set; } = Array.Empty<int>();
        public int[] AbsoluteEpisodeNumbers { get; set; } = Array.Empty<int>();
        public bool IsDaily { get; set; }
        public bool IsAbsoluteNumbering { get; set; }
        public bool IsPossibleSpecialEpisode { get; set; }
        public bool Special { get; set; }
    }

    // Mirrors frontend Quality.ts QualityModel `{ quality: Quality, revision: Revision }`
    // (frontend/src/Quality/Quality.ts:18-21). Stub-shaped because manga has no quality model.
    public class StubQualityModel
    {
        public StubQuality Quality { get; set; } = new StubQuality();
        public StubRevision Revision { get; set; } = new StubRevision();
    }

    public class StubQuality
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Unknown";
    }

    public class StubRevision
    {
        public int Version { get; set; } = 1;
        public int Real { get; set; }
        public bool IsRepack { get; set; }
    }

    public static class ParsedChapterInfoResourceMapper
    {
        public static ParsedChapterInfoResource ToResource(this ParsedChapterInfo parsedChapterInfo, string? scanlationGroup)
        {
            // Cast decimal chapter numbers to int for the frontend's TV-flavored EpisodeNumbers
            // contract. Whole-chapter releases survive the cast; decimal-chapter releases lose
            // the fractional component but retain a usable label (the row only displays the
            // value via ReleaseSceneIndicator which is itself a Phase 15 stub).
            var episodeNumbers = parsedChapterInfo.ChapterNumbers != null
                ? parsedChapterInfo.ChapterNumbers.Select(c => (int)c).ToArray()
                : Array.Empty<int>();

            var absoluteEpisodeNumbers = parsedChapterInfo.AbsoluteChapterNumber.HasValue
                ? new[] { (int)parsedChapterInfo.AbsoluteChapterNumber.Value }
                : Array.Empty<int>();

            return new ParsedChapterInfoResource
            {
                Quality = new StubQualityModel(),
                ReleaseGroup = scanlationGroup ?? parsedChapterInfo.ScanlationGroup,
                ReleaseHash = string.Empty,
                FullSeason = false,
                SeasonNumber = 0,
                AirDate = null,
                SeriesTitle = parsedChapterInfo.MangaTitle,
                EpisodeNumbers = episodeNumbers,
                AbsoluteEpisodeNumbers = absoluteEpisodeNumbers,
                IsDaily = false,
                IsAbsoluteNumbering = false,
                IsPossibleSpecialEpisode = false,
                Special = parsedChapterInfo.ChapterType != ChapterType.Regular,
            };
        }
    }
}
