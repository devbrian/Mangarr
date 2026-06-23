# NzbDrone.Core/Discovery

## Purpose

The core business logic for the **Discovery** filtered-bulk-add browse vertical (Phase 42): the eligibility auto-paging loop over the MangaBaka attribute API, the filter model, the cached genre/tag option lists, and the fire-and-forget bulk-add command. NEW-in-Mangarr; no Sonarr peer (see [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Discovery\`

## Key Files

| File | Purpose |
|------|---------|
| `IDiscoveryService.cs` / `DiscoveryService.cs` | `Search(DiscoveryFilter, x)` eligibility auto-paging loop + `GetGenres()` / `GetTags()` cached option lists (12h TTL). The highest unit-test surface of the vertical. |
| `DiscoveryFilter.cs` | The browse query target: type/genre/status/contentRating include+exclude lists, tag id lists + `tag_mode`, year/score ranges, sort, `IncludeAdult`. |
| `DiscoveryResult.cs` | `DiscoveryResult` envelope (`Results` + `PoolExhausted` + `Requested` + `Found`) + `DiscoveryResultItem`. |
| `DiscoveryBulkAddCommand.cs` / `DiscoveryBulkAddCommandExecutor.cs` | Fire-and-forget bulk-add: `List<int> MangaBakaIds` + add-options → `AddManga(List<Manga>)` with per-item failure isolation + command-queue progress. |

## How it works

### MangaBaka resolution (no DI registration for `MangaBakaApi`)

`MangaBakaApi` is NOT DI-registered — it is constructed lazily inside `MangaBakaMetadataSource` (Phase 41). So `DiscoveryService` reaches the browse surface through the provider's public `Browse` / `GetGenres` / `GetTags` pass-throughs, resolving the provider from the DryIoc-registered `IEnumerable<IMetadataSource>` via `OfType<MangaBakaMetadataSource>().Single()`. It resolves MangaBaka **specifically** (not the active-primary resolver) because only MangaBaka's search supports attribute filters — Discovery browses MangaBaka regardless of the active primary. `Single()` is intentional: exactly one MangaBaka provider is registered; a 0/2 count is a wiring bug to surface loudly.

### Eligibility auto-paging loop (`Search`)

1. Builds two up-front `HashSet<int>` (in-library MangaBakaIds + excluded MangaBakaIds) for O(1) per-row eligibility — the DB reads happen once, not per page.
2. Safe-by-default adult filter (D-04/D-05/T-42-02-ADULT): when `IncludeAdult` is false, injects `content_rating=safe&suggestive`. The caller's filter is cloned, never mutated.
3. Pages `1..MaxPage` (`Limit=100`, `MaxPage=100` ceiling guarantees termination — T-42-02-DOS). Skips merged/deleted record stubs (D-12) + in-library + excluded rows; collects eligible items.
4. Stops at X eligible (WITHOUT marking the pool exhausted — more may remain); sets `poolExhausted` on an empty page, on computed exhaustion (`page*Limit >= total`), or on the MaxPage ceiling.

### Throttling lives ENTIRELY in the HTTP layer

`MangaBakaApi.Browse` (the Phase-42 extension on the Phase-41 provider's `MangaBakaApi`) sets `RateLimit` on the shared `mangabaka` bucket (`RateLimitKey="mangabaka"`, isolated from MangaDex's `mangadex` budget). The loop performs **no in-loop sleep of any kind** — the rate-limited paging self-throttles (quick-260621-rt9 fixed the missing-RateLimit bug; setting `RateLimitKey` without `RateLimit` is Pitfall 1, a no-op that lets pages fire back-to-back → 429).

### Bulk-add rides the command queue

`DiscoveryBulkAddCommand` + `DiscoveryBulkAddCommandExecutor` give progress + cancel + per-item failure capture for free; the FE already watches `['/command']`. The refresh fan-out self-paces through the existing `LookupRateLimit` (0.5s) on `GetById` — no new throttle.

## Tests

- `NzbDrone.Core.Test/Discovery/DiscoveryServiceFixture.cs` — eligibility loop (exactly-X, fewer-than-X→poolExhausted, all-filtered-first-page, ceiling-hit, merged/deleted skip) + browse param building + adult-default injection + genres/tags cache (DISC-02/03/05/06/10).
- `NzbDrone.Core.Test/Discovery/DiscoveryBulkAddCommandExecutorFixture.cs` — bulk-add + per-item failure isolation (DISC-07).
- Live E2E: `src/NzbDrone.Automation.Test/Discovery/DiscoveryUiFixture.cs` (Plan 42-08).

## Cross-References

- [src/Mangarr.Api.V5/Discovery/CLAUDE.md](../../Mangarr.Api.V5/Discovery/CLAUDE.md) — the REST surface over this service.
- [frontend/src/Discovery/CLAUDE.md](../../../frontend/src/Discovery/CLAUDE.md) — the consumer.
- [src/NzbDrone.Core/MetadataSource/MangaBaka/CLAUDE.md](../MetadataSource/MangaBaka/CLAUDE.md) — `MangaBakaApi` + the `mangabaka` rate bucket this extends.
- [src/NzbDrone.Core/ImportLists/CLAUDE.md](../ImportLists/CLAUDE.md) — the global `ImportListExclusion` model per-card Exclude reuses.
- [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42 — the Discovery vertical divergence.
