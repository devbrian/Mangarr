# NzbDrone.Core.Test/Indexers (Manga Indexer Test Tree)

## Purpose

Unit fixtures for the manga indexer layer. Since Phase 39 the sole `IIndexer` is
`GatewayIndexer` (Phase 37) — the in-process MangaDex + comix.to site-scraper indexers
(and their `MangaDex/` + `Comix/` fixture subtrees) were RETIRED in Phase 39 Plan 39-03.
The legacy TV `IndexerTests/` tree (Newznab, Nyaa, Torznab, …) was deleted in the Phase 15
TV-test removal.

**Absolute Path**: `src/NzbDrone.Core.Test/Indexers`

## Key Files

| File | Purpose |
|------|---------|
| `Gateway/GatewayIndexerFixture.cs` | Core `GatewayIndexer` behavior — search, fetch, capability checks. |
| `Gateway/GatewayParserFixture.cs` | Gateway response → `ReleaseInfo` parsing (chapter number, language, scanlation group). |
| `Gateway/GatewayRequestGeneratorFixture.cs` | URL/query composition + paging (offset/limit stride). |
| `Gateway/GatewayIndexerPagingFixture.cs` | Offset-paging coverage (gateway honors `offset` since ~2026-06-20). |
| `Gateway/GatewayCapabilitiesProviderFixture.cs` | Capability negotiation against the gateway. |
| `Gateway/GatewayIndexerTestFixture.cs` | `Test()` connection probe behavior. |
| `Gateway/GatewaySettingsValidatorFixture.cs` | `GatewaySettings` FluentValidation rules. |
| `IndexerFactory/InitializeProvidersFixture.cs` | Default-provider seeding (gateway seeded disabled-by-default). |
| `IndexerFactory/ResolveIndexerFixture.cs` | Definition → indexer instance resolution. |
| `IndexerRepositoryFixture.cs` | Indexer definition persistence. |
| `IndexerSourceStatusServiceFixture.cs` | SOURCE-05 D-17 — per-SourceKey escalation; mirrors `IndexerStatusServiceFixture` shape. |
| `SharedSourceKeyDisableFixture.cs` | D-17 — two indexer instances with the same SourceKey share disable state. |

## Patterns / Conventions

- All fixtures use `CoreTest<TSubject>` + Moq + FluentAssertions per the inherited Sonarr precedent (the TV `IndexerTests/NewznabTests/NewznabFixture.cs` shape this was modelled on was deleted in the Phase 15 TV-test removal; cited for provenance only, absent at HEAD).
- JSON fixtures (where used) load via `File.ReadAllText`; the `.csproj` `<None Update="Files\**\*.*">` glob copies them to test output.

## Cross-References

- Production code: `src/NzbDrone.Core/Indexers/Gateway/` (sole `IIndexer`)
- Metadata-source sibling (NOT an indexer — kept): `src/NzbDrone.Core.Test/MetadataSource/MangaDex/MangaDexMetadataSourceFixture.cs`
- Phase 39 retirement: `.planning/phases/39-retire-in-process/` (Plan 39-03 deleted the in-process MangaDex/Comix indexers + their fixtures)
