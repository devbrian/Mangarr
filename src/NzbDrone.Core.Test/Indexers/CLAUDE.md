# NzbDrone.Core.Test/Indexers (Manga Indexer Test Tree)

## Purpose

Unit fixtures for Phase 3 manga aggregator indexers (MangaDex, comix.to). Sibling to the existing `IndexerTests/` directory which holds TV indexer fixtures (Newznab, Nyaa, Torznab, etc.). Phase 8 will collapse both trees when TV indexers are deleted.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core.Test\Indexers`

## Key Files

| File | Purpose |
|------|---------|
| `MangaDex/MangaDexIndexerFixture.cs` | SOURCE-01/03/04/07; D-01..D-03 NotSupportedException for TV criteria overloads; D-14 GetDownloadHeaders |
| `MangaDex/MangaDexParserFixture.cs` | SOURCE-04 ScanlationGroup + TranslatedLanguage + decimal ChapterNumber extraction (loads `Files/Indexers/MangaDex/feed_*.json`) |
| `MangaDex/MangaDexRequestGeneratorFixture.cs` | URL composition + paging (limit=500); TV criteria overloads return empty chain |
| `MangaDex/MangaDexIndexerSettingsHonestUaFixture.cs` | T-CONFIG-DRIFT-01 — reflection assertion: NO `[FieldDefinition]` on `UserAgentOverride` (Pitfall 4 mitigation) |
| `MangaDex/MangaDexSharedBudgetFixture.cs` | Pitfall 5 — `SourceKey == "mangadex"` shared with Phase 2 metadata source for budget pooling |
| `Comix/ComixIndexerFixture.cs` | SOURCE-01/02/03; D-14 GetDownloadHeaders returns Referer |
| `Comix/ComixParserFixture.cs` | SOURCE-04 (ScanlationGroup when present, TranslatedLanguage="en" hard-coded) |
| `Comix/ComixRequestGeneratorFixture.cs` | URL composition for `/api/v2/manga` + `/api/v2/manga/{hash}/chapters` |
| `IndexerSourceStatusServiceFixture.cs` | SOURCE-05 D-17 — per-SourceKey escalation; mirrors `IndexerStatusServiceFixture` shape |
| `SharedSourceKeyDisableFixture.cs` | D-17 intent — two indexer instances with same SourceKey share disable state |

## Patterns / Conventions

- All fixtures use `CoreTest<TSubject>` + Moq + FluentAssertions per the inherited Sonarr precedent (the TV `IndexerTests/NewznabTests/NewznabFixture.cs` that this shape was modelled on was deleted in the Phase 15 TV-test removal; the live siblings are the `Indexers/Comix/` + `Indexers/MangaDex/` fixtures).
- JSON fixtures load via `File.ReadAllText("Files/Indexers/{MangaDex,Comix}/...json")`; the `.csproj` `<None Update="Files\**\*.*">` glob copies them to test output.
- ToS reflection assertions (Pitfall 4) are CRITICAL — adding `[FieldDefinition]` to MangaDex UA override is a ban-risk regression.
- Tests start RED at Wave 0; flip GREEN as Plans 03-02..03-05 land production code. The Wave 0 commit is INTENTIONALLY a compile-fail for many tests because the production types do not exist yet — this is the "lock the contract before implementation" Nyquist pattern from `03-VALIDATION.md`.

## Manga Adaptation Notes

- `Indexers/` is the final manga indexer test tree at HEAD. The legacy TV `IndexerTests/` directory was deleted in the Phase 15 TV-test removal (the planned Phase 8 collapse / symmetry merge was overtaken by that deletion).

## Cross-References

- Sonarr analog (the TV indexer-fixture shape this was modelled on): `NzbDrone.Core.Test/IndexerTests/NewznabTests/NewznabFixture.cs` — DELETED in the Phase 15 TV-test removal; cited for provenance only, absent at HEAD (path shown repo-relative-from-`src/` since it no longer resolves).
- Phase 2 sibling: `src/NzbDrone.Core.Test/MetadataSource/MangaDex/MangaDexMetadataSourceFixture.cs`
- Production code (lands in Plans 03-02..03-05): `src/NzbDrone.Core/Indexers/{MangaDex,Comix}/`
- Validation map: `.planning/phases/03-indexer-contract-aggregator-sources/03-VALIDATION.md`
- Fixture provenance: `.planning/phases/03-indexer-contract-aggregator-sources/SOURCE-PROBE-fixtures.md`
