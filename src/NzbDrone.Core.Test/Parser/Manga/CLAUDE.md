# Parser/Manga Test Fixtures

## Purpose

Manga parser test fixtures + the 500-title corpus that gates Phase 2 completion. Every Phase 2 parser deliverable is verified here before downstream plans (`02-02`..`02-10`) are allowed to flip from RED to GREEN.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core.Test\Parser\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `test_corpus_v1.json` | 500-entry MangaDex `/chapter` feed corpus committed in-tree per D-07. Schema documented inline in each entry. |
| `MangaParserCorpusFixture.cs` | Loads `test_corpus_v1.json` via `File.ReadAllText`; asserts ≥95% parse rate (D-08 gate). |
| `MangaParsingServiceFixture.cs` | `MangaParsingService.Map(ParsedChapterInfo, Manga, IList<Chapter>) → RemoteChapter` resolution + D-10 indexer-language-wins precedence. |
| `MangaTitleNormalizerFixture.cs` | `[TestCase]` rows for D-05 canonicalization (NFKD fullwidth, diacritics, punctuation, alt-title parenthetical drop, CJK preservation). |
| `MangaLanguageParserFixture.cs` | `[TestCase]` rows for D-04 + LANG-01 BCP-47 extraction from `[EN]`, `(Spanish)`, `[ja]` markers. |
| `MangaScanlationGroupParserFixture.cs` | `[TestCase]` rows for D-04 `^[Group]` extraction. |
| `MangaParserRegressionFixture.cs` | Targeted `[TestCase]` regressions (e.g. code-review BL-03/BL-04) — the corpus gate only asserts ≥95% non-null parse rate, so per-entry mis-classification regressions need explicit rows here. |

## Patterns / Conventions

- **TestCase rows for unit fixtures.** Modelled on the Sonarr TV `NzbDrone.Core.Test/ParserTests/ParserFixture.cs` (DELETED in the Phase 15 TV-test removal; cited for provenance only, absent at HEAD — path shown repo-relative-from-`src/` since it no longer resolves). Use one `[TestCase(input, expected)]` line per scenario.
- **`CoreTest<TSubject>` for service fixtures.** Modelled on the Sonarr TV `NzbDrone.Core.Test/TvTests/AddSeriesFixture.cs` (DELETED in the Phase 15 TV-test removal; cited for provenance only, absent at HEAD — path shown repo-relative-from-`src/` since it no longer resolves). `Subject` resolves `MangaParsingService`; mock `IMangaService` + `IChapterService` via `Mocker.GetMock<T>()`.
- **`File.ReadAllText` + `Path.Combine(TestContext.CurrentContext.TestDirectory, ...)`** for the corpus JSON. Per RESEARCH.md Pitfall 8 the corpus is read once on disk and never regenerated at test time.
- **Newtonsoft.Json + `JsonProperty` attributes** to deserialize the snake_case corpus schema into a private `CorpusEntry` POCO with PascalCase fields.

## Manga Adaptation Notes

- All fixtures are leaf-most Phase 2 verification gates. They start RED — the production types (`MangaParser`, `MangaParsingService`, `MangaTitleNormalizer`, `MangaLanguageParser`, `MangaScanlationGroupParser`) live as Wave 0 stubs that throw `NotImplementedException` until Plan 02-04 lands their implementations.
- Phase 8 will move the entire `Parser/Manga/` tree up one level (becomes the new `Parser/`) and delete the TV peers. **No rename within Phase 2** per the leaf-first / rename-last roadmap order.

## TODO

> **`test_corpus_v1.json` is committed in-tree per D-07.** Manual refresh via `scripts/regenerate-manga-corpus.ps1` (NOT run at test time per RESEARCH.md Pitfall 8). When refreshing, preserve the composition floor: ≥350 en + ≥50 es + ≥30 ja + ≥30 raw + ≥40 with decimal chapter numbers + ≥20 Extras + ≥10 oneshots. The 95% parse-rate gate (D-08) must still hold against the regenerated corpus before merging.

## Cross-References

- [Phase 2 Plan 02-01](../../../../.planning/phases/02-parser-metadata-sources/02-01-PLAN.md) — Wave 0 fixture creation
- [Phase 2 CONTEXT.md](../../../../.planning/phases/02-parser-metadata-sources/02-CONTEXT.md) — D-01..D-13 parser layout & corpus decisions
- [Phase 2 VALIDATION.md](../../../../.planning/phases/02-parser-metadata-sources/02-VALIDATION.md) — per-task verification map
- [Phase 2 PATTERNS.md](../../../../.planning/phases/02-parser-metadata-sources/02-PATTERNS.md) — Group 1 (Parser tree analogs)
