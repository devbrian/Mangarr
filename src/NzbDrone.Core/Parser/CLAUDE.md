# NzbDrone.Core/Parser

## Purpose

The **release-title parsing engine** — converts raw release titles (and filenames) into structured metadata. The Sonarr→Mangarr migration of this engine SHIPPED in Phase 2/6: the TV/anime parser (`Parser.cs`, `ParsingService.cs`, `QualityParser.cs`, `LanguageParser.cs`) was removed and the manga parser now lives under `Parser/Manga/`. The notes below preserve the TV-parser shape for migration provenance, but the live engine at HEAD is the manga one.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Parser\`

## Files (live at HEAD)

| File | Purpose |
|------|---------|
| `Manga/MangaParser.cs` | Central manga regex parser (chapter/volume numbers, scanlation groups, ranges). Replaced the deleted TV `Parser.cs`. |
| `Manga/MangaParsingService.cs` | Maps the manga parse result → `RemoteChapter` (resolves Manga + Chapters via `IMangaService` / `IChapterService`). Replaced the deleted TV `ParsingService.cs`. |
| `Manga/MangaLanguageParser.cs` | BCP-47 language extraction from `[EN]` / `(Spanish)` / `[ja]` markers. Replaced the deleted TV `LanguageParser.cs`. |
| `Manga/MangaScanlationGroupParser.cs` | `^[Group]` scanlation-group extraction. Replaced the deleted TV `ReleaseGroupParser.cs` role for manga. |
| `Manga/MangaTitleNormalizer.cs` | Manga title normalization. |
| `Manga/ChapterType.cs` | Chapter-type enum (Special / Extra / Oneshot / etc.). |
| `ReleaseGroupParser.cs` | Shared release-group / scene-tag helper (still present at HEAD). |
| `IsoLanguages.cs` / `IsoLanguage.cs` | Language-code lookup. |
| `ParserCommon.cs` / `RegexReplace.cs` | Shared regex helpers. |

> The TV files below (`Parser.cs`, `ParsingService.cs`, `IParsingService.cs`, `QualityParser.cs`, `LanguageParser.cs`) were DELETED in the Sonarr→Mangarr migration; the API/struct/pipeline snippets that follow are preserved as migration provenance, not as a description of HEAD.

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

### Approach taken (SHIPPED in Phase 2/6 — historical record)

1. A new `Manga/MangaParser.cs` was added alongside the TV `Parser.cs` (leaf-first), then the TV parser was deleted once the manga path was stable.
2. New manga model types landed: `RemoteChapter` (`Parser/Manga/Model/`) plus the manga parse-result shape consumed by `MangaParsingService`.
3. `MangaParsingService` returns `RemoteChapter`; the manga decision pipeline (`DecisionEngine/Manga/`) consumes it.
4. The TV-only regex/code paths were removed in the Phase 15 `Tv/` deletion.

### New Tokens to Parse
- `Ch.042`, `c042`, `Chapter 42`, ` 042 ` (bare number)
- `Vol.5`, `v05`, `Volume 5`
- Decimal chapters: `042.5`, `042.1`
- Group prefix in brackets: `[GroupName]`
- Quality: `[Color]`, `[B&W]`, `[Official]`, `[HQ]`, `[LQ]`, DPI marks
- Languages embedded: `[English]`, `[JP]`, `[KO]`
- Special types: `[OMNIBUS]`, `[Special]`, `[Extra]`, `[Side Story]`, `[Oneshot]`

## Tests

The parser is one of the **most heavily tested** parts of the codebase. The manga parser tests live in:
- `src/NzbDrone.Core.Test/Parser/Manga/MangaParserCorpusFixture.cs` — corpus-driven title parsing
- `src/NzbDrone.Core.Test/Parser/Manga/MangaParserRegressionFixture.cs` — regression cases
- `src/NzbDrone.Core.Test/Parser/Manga/MangaParsingServiceFixture.cs` — parse → `RemoteChapter` mapping
- `src/NzbDrone.Core.Test/Parser/Manga/MangaLanguageParserFixture.cs` — BCP-47 language extraction
- `src/NzbDrone.Core.Test/Parser/Manga/MangaScanlationGroupParserFixture.cs` — scanlation group
- `src/NzbDrone.Core.Test/Parser/Manga/MangaTitleNormalizerFixture.cs` — title normalization

(The Sonarr TV `src/NzbDrone.Core.Test/ParserTests/` fixtures — `QualityParserFixture`, `LanguageParserFixture`, `ReleaseGroupParserFixture`, etc. — were deleted in the Phase 15 TV-test removal.)

When adding patterns, add a TestCase row to the relevant fixture covering both positive and negative examples.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [./Manga/CLAUDE.md](./Manga/CLAUDE.md) — the live manga parser tree (`MangaParser` + `MangaParsingService`) that resolves Manga/Chapter (the Sonarr `Tv/` analog was deleted in Phase 15)
- [../DecisionEngine/Manga/CLAUDE.md](../DecisionEngine/Manga/CLAUDE.md) — Consumer of `RemoteChapter`
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — Source of `ReleaseInfo`
- [../DataAugmentation/](../DataAugmentation/) — Scene mapping / alternate-title overrides
