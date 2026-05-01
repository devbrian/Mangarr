# NzbDrone.Core/Parser

## Purpose

The **release-title parsing engine** — converts raw release titles (and filenames) into structured metadata. This is one of the **most critical Sonarr→Mangarr migration targets**: the parser is exclusively built around TV/anime release naming conventions.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Parser\`

## Files

| File | Purpose |
|------|---------|
| `Parser.cs` | **~71 KB** central regex parser. Static class. The bulk of the project's regex patterns. |
| `ParsingService.cs` | Maps `ParsedEpisodeInfo` → `RemoteEpisode` (resolves Series + Episodes from DB) |
| `IParsingService.cs` | Interface |
| `QualityParser.cs` | Extract video quality (480p/720p/1080p/2160p/REMUX/etc.) from a title |
| `LanguageParser.cs` | Extract language(s) |
| `ReleaseGroupParser.cs` | Extract release-group / scene tag |
| `IsoLanguages.cs` | Language code lookup |

### Subdirectories
- `Model/` — DTOs produced/consumed by the parser:
  - `ReleaseInfo.cs` — Indexer-supplied release (from RSS/search)
  - `RemoteEpisode.cs` — `ReleaseInfo` + matched `Series` + `List<Episode>`
  - `ParsedEpisodeInfo.cs` — Output of regex pass: titles, season/episode numbers, language, quality, release group, etc.
  - `ParsedSeriesInfo.cs` — Lighter title-only parse
  - `ParsedTrackInfo.cs`, `ParsedMovieInfo.cs` (if present, vestigial)
  - `TorrentInfo.cs` — Specialized info for torrent releases
  - `RemoteSeries.cs` — Series-only resolution wrapper

## Parser.cs — Public API

```csharp
public static class Parser
{
    public static ParsedEpisodeInfo ParsePath(string path);
    public static ParsedEpisodeInfo ParseTitle(string title);
    public static string ParseSeriesName(string title);

    public static string SimplifyTitle(string title);       // strip noise
    public static string CleanSeriesTitle(this string title); // extension method
    public static string NormalizeEpisodeTitle(string title);
    public static string NormalizeTitle(string title);
    public static string NormalizeImdbId(string imdbId);
    public static string RemoveFileExtension(string title);
    public static bool HasMultipleLanguages(string title);
}
```

The internals are dozens of regexes covering:
- `[Show Name].S01E02.Title.1080p.WEB-DL.x264-GROUP`
- `Show.Name.S01E02E03` (multi-episode)
- `[Subgroup] Show Name - 01 [1080p][Hi10P][AAC][CRC32].mkv` (anime)
- `Show.Name.2024.01.15.1080p.WEB.h264-GROUP` (daily)
- `Show.Name.S01.Complete.Pack-GROUP` (season pack)
- Roman numerals, "Special", "OVA", "Movie", "Episode" word forms, etc.

## ParsedEpisodeInfo — Structure

```csharp
public class ParsedEpisodeInfo
{
    public string ReleaseTitle { get; set; }         // original
    public string SeriesTitle { get; set; }
    public SeriesTitleInfo SeriesTitleInfo { get; set; }  // year-aware fallback
    public QualityModel Quality { get; set; }
    public List<Language> Languages { get; set; }
    public int SeasonNumber { get; set; }
    public int[] EpisodeNumbers { get; set; }
    public int[] AbsoluteEpisodeNumbers { get; set; }
    public DateTime? AirDate { get; set; }
    public string AirDateString { get; set; }
    public bool FullSeason { get; set; }
    public bool IsPartialSeason { get; set; }
    public bool IsMultiSeason { get; set; }
    public bool Special { get; set; }
    public string ReleaseGroup { get; set; }
    public string ReleaseHash { get; set; }
    public string ReleaseTokens { get; set; }
    public List<IndexerFlag> IndexerFlags { get; set; }
    public ReleaseType ReleaseType { get; set; }
}
```

## Pipeline

```
Indexer release title (string)
       ↓
Parser.ParseTitle(title)
       ↓
ParsedEpisodeInfo  (or null if unparseable)
       ↓
ParsingService.Map(parsedInfo, indexerId, etc.)
       ↓ tries:
       │  - exact title match (CleanTitle)
       │  - scene mapping (DataAugmentation/)
       │  - alternate titles
       │  - tvdbId / tvRageId hints
       ↓
RemoteEpisode  (ParsedEpisodeInfo + matched Series + matched Episodes)
       ↓
DecisionEngine.GetDecision(remoteEpisode)
```

## Manga Adaptation: Strategy

This is one of the **highest effort** components of the migration. Manga release titles have a different shape:

```
[Scanlation Group] Series Name - Chapter 042 [Volume 5][Color][1080p].cbz
[Group] Series Name v05 c042 [+OMNIBUS][Sample]
Series Name Vol.05 Ch.042 - Chapter Title (Group) [English]
Series Name 042 [English] [Group]
Series Name - 042.5 (Special) [Group]
```

### Key Differences vs TV
| TV/Anime | Manga |
|----------|-------|
| `S01E02` | `Vol.05 Ch.042` or `v05c042` or just `042` |
| Quality (1080p, WEB-DL) | DPI / official vs scan vs raw |
| Release group (NTb, EVO) | Scanlation group |
| Multi-episode `E02E03` | Multi-chapter `c042-045` |
| Season pack `S01.Complete` | Volume pack `v05` (whole volume) or "complete" |
| Air date `2024.01.15` | Release date (often missing or just year) |
| Absolute episode numbering (anime) | Chapter numbering (closest analog) |

### Recommended Approach

1. **Don't replace `Parser.cs` in-place.** Add a new `MangaParser.cs` (or `ChapterParser.cs`) alongside it. Have a feature flag or media-type switch to choose.
2. Add new model types: `ParsedChapterInfo`, `RemoteChapter`, `ParsedMangaInfo`.
3. Update `ParsingService` to have a manga code path that returns `RemoteChapter`.
4. Migrate `DecisionEngine` specs to operate on a base `RemoteRelease<TItem>` once both code paths exist.
5. Once stable, delete TV-only regex patterns from `Parser.cs`.

### New Tokens to Parse
- `Ch.042`, `c042`, `Chapter 42`, ` 042 ` (bare number)
- `Vol.5`, `v05`, `Volume 5`
- Decimal chapters: `042.5`, `042.1`
- Group prefix in brackets: `[GroupName]`
- Quality: `[Color]`, `[B&W]`, `[Official]`, `[HQ]`, `[LQ]`, DPI marks
- Languages embedded: `[English]`, `[JP]`, `[KO]`
- Special types: `[OMNIBUS]`, `[Special]`, `[Extra]`, `[Side Story]`, `[Oneshot]`

## Tests

Parser is the **most heavily tested** part of the codebase. Tests live in:
- `src/NzbDrone.Core.Test/ParserTests/` — title parsing
- `src/NzbDrone.Core.Test/ParserTests/QualityParserFixture.cs` — quality
- `src/NzbDrone.Core.Test/ParserTests/LanguageParserFixture.cs` — language
- `src/NzbDrone.Core.Test/ParserTests/ReleaseGroupParserFixture.cs` — group

When adding patterns, add a TestCase row to the relevant fixture covering both positive and negative examples.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Tv/CLAUDE.md](../Tv/CLAUDE.md) — Series/Episode that ParsingService resolves to
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Consumer of `RemoteEpisode`
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — Source of `ReleaseInfo`
- [../DataAugmentation/](../DataAugmentation/) — Scene mapping / alternate-title overrides
