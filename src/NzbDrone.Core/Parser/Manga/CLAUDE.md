# Parser/Manga

## Purpose

Pure-function manga release-title parser + DB-mapping service. Sibling to the existing `Parser/` (TV) tree per Phase 2 02-CONTEXT.md **D-01**; never overwrites a TV-parser file. Phase 8 cutover: this directory moves up to become the canonical `Parser/` and the TV peers are deleted.

The parser is the leaf-most computation layer Phase 2 ships — every later phase (Phase 3 indexers, Phase 4 archive layer, Phase 5 Decision Engine + Custom Formats) consumes the DTOs produced here.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Parser\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaParser.cs` | Static class; `ParseChapterTitle(title) -> ParsedChapterInfo`. Compiled regex set; corpus gate `>=95%` non-null on 500-title MangaDex corpus per **D-08** (currently passes 500/500) |
| `MangaLanguageParser.cs` | Static class; `ParseLanguage(title) -> BCP-47 string` (en/es/ja/...) per **D-04** + LANG-01. Spelled-out names win over bracketed tags so `[Mangastream]` is correctly skipped over in favor of `[EN]` |
| `MangaScanlationGroupParser.cs` | Static class; `ParseScanlationGroup(title) -> string`. Anime-style `^[<group>]` regex per **D-04** — mirrors `AnimeReleaseGroupRegex` from `ReleaseGroupParser.cs:14` verbatim |
| `MangaTitleNormalizer.cs` | Static class; `Normalize(title) -> canonical form`. Lowercase + NFKD + diacritic strip + punctuation strip (no-replacement-char) + trailing parenthetical drop per **D-05**. Single source of truth used by AddMangaService dedup AND CrossSourceIdResolver (Plan 02-09) |
| `MangaParsingService.cs` | DI-resolved service; `Map(ParsedChapterInfo, Manga, IList<Chapter>) -> RemoteChapter` per **D-03**. Honors **D-10** (indexer-supplied TranslatedLanguage wins; parser fallback). The single entrypoint Phase 3 indexers call after parsing a release title |
| `ChapterType.cs` | Enum `{Regular, Extra, Bonus, SideStory, Oneshot, Prologue, Epilogue, Special}` per **D-09** |
| `Model/ParsedChapterInfo.cs` | Parser output DTO per **D-09**. `decimal[] ChapterNumbers`, `ChapterType`, BCP-47 `TranslatedLanguage`, raw `ReleaseTitle` preserved verbatim for Phase 5 CF regex matching |
| `Model/RemoteChapter.cs` | Resolver output DTO; Phase 3 indexer hook point. Pairs `ParsedChapterInfo` + resolved `Manga` + matched `Chapter[]` |

## Patterns / Conventions

- `public static class` with `static readonly Regex[]` (`RegexOptions.Compiled`) per **D-02** + **D-06**.
- Synchronous parse method — no async (**D-06**).
- Sub-parsers (`MangaLanguageParser`, `MangaScanlationGroupParser`, `MangaTitleNormalizer`) are SIBLINGS to existing TV peers in `Parser/` — do NOT modify the TV files (**D-04**). Phase 8 deletes the TV peers.
- `ParsedChapterInfo` carries raw `ReleaseTitle` verbatim per **D-09** — Phase 5 Custom Format regex matches against it without parser canonicalization.
- Decimal-format normalization is aggressive per **D-13** (`14,5` / `14_5` / `14-5` -> `14.5` in digit-direct-digit context). Multi-chapter ranges like `Ch.10-12` collapse to `Ch.10.12` after this pass; the corpus contains no such ranges. If ranges become a real-world need, scan with the multi-chapter-range regex BEFORE applying the normalization.
- File-extension stripping uses an allowlist (`.cbz` / `.cbr` / `.cb7` / `.cbt` / `.zip` / `.rar` / `.pdf` / `.epub`) NOT `Path.GetExtension` alone, because `Path.GetExtension("Ch.1")` returns `.1` and would silently delete the chapter number.
- Language-precedence rule (**D-10**): `MangaParsingService.Map` does NOT re-derive the parsed language. Phase 3 indexers MUST overwrite `parsedInfo.TranslatedLanguage` with the API-supplied value BEFORE invoking `Map()` when the indexer has it. The PARSER-EXTRACTED value is fallback only.
- BCP-47 sentinel: `"und"` (RFC 5646 / ISO 639-2) when neither indexer nor parser supplies a language. Synthetic chapter rows (Plan 02-03 `ChapterRepository`) use the same sentinel — keeping the value space consistent across resolver and synthesis paths.

## Manga Adaptation Notes

- DO NOT modify `src/NzbDrone.Core/Parser/Parser.cs`, `LanguageParser.cs`, `ReleaseGroupParser.cs`, `Model/ParsedEpisodeInfo.cs` — Phase 8 owns the rename. The Phase 2 parser tree is **already manga-named**, so Phase 8 is move-up + delete-TV-peers, NOT search-replace.
- Corpus is committed in-tree at `src/NzbDrone.Core.Test/Parser/Manga/test_corpus_v1.json` per **D-07**; refresh is a manual operation via `scripts/regenerate-manga-corpus.ps1` (Phase 0 RESEARCH.md Pitfall 8).
- The two type-vs-namespace collisions (`Manga.Manga` ambiguous inside `NzbDrone.Core.Parser.Manga`) are resolved by full qualification (`NzbDrone.Core.Manga.Manga`) in service signatures. Phase 8 rename eliminates this when `NzbDrone.Core.Parser.Manga` collapses to `NzbDrone.Core.Parser`.

## Cross-References

- Manga model: `src/NzbDrone.Core/Manga/CLAUDE.md`
- Sonarr TV analog (untouched until Phase 8): `src/NzbDrone.Core/Parser/CLAUDE.md`
- Test fixtures: `src/NzbDrone.Core.Test/Parser/Manga/CLAUDE.md`
- Phase 2 context: `.planning/phases/02-parser-metadata-sources/02-CONTEXT.md` (D-01..D-13 parser decisions)
- Phase 2 corpus rationale: `.planning/phases/02-parser-metadata-sources/02-RESEARCH.md`
- Phase 2 patterns: `.planning/phases/02-parser-metadata-sources/02-PATTERNS.md` (Group 1 — Parser tree analogs)
