# Mangarr.Api.V5/Discovery

## Purpose

The REST surface for the **Discovery** filtered-bulk-add browse vertical (Phase 42). A bare `[V5ApiController("discovery")]` wrapping `IDiscoveryService` ([NzbDrone.Core/Discovery](../../NzbDrone.Core/Discovery/CLAUDE.md)) + `IManageCommandQueue`, exposing four admin-authenticated endpoints the Discovery frontend ([frontend/src/Discovery](../../../frontend/src/Discovery/CLAUDE.md)) consumes.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Mangarr.Api.V5\Discovery\`

## Key Files

| File | Purpose |
|------|---------|
| `DiscoveryController.cs` | Bare `[V5ApiController("discovery")]`, 4 endpoints. Admin `X-Api-Key` on all (T-42-04-AUTHN). |
| `DiscoverySearchRequestResource.cs` | Allow-list filter DTO + `ToFilter()` → Core `DiscoveryFilter` (mass-assignment mitigation, T-42-04-MASS). |
| `DiscoveryResource.cs` | `DiscoverySearchResponseResource` + `DiscoveryResultResource` + `DiscoveryGenreResource` + `DiscoveryTagResource` + mappers. |
| `DiscoveryBulkAddResource.cs` | Strict 7-field allow-list bulk-add payload (`mangaBakaIds`, `rootFolderPath`, `monitor`, `translationProfileId`, `customFormatProfileId`, `tags`, `searchForMissingChapters`). |
| `DiscoverySearchRequestValidator.cs` | `X` `InclusiveBetween(1,100)`; enum-whitelist `.Must(...)` for type/typeNot/status/statusNot/contentRating/sortBy/tagMode; year `1679-2262` + score `0-100` range bounds gated `.When(set)`. |

## Endpoints

| Method + Route | Behavior |
|----------------|----------|
| `GET /api/v5/discovery/genres` | Cached slim genre option list (`IDiscoveryService.GetGenres()` → `DiscoveryGenreResource`). |
| `GET /api/v5/discovery/tags` | Cached slim tag option list (`GetTags()` → `DiscoveryTagResource`). |
| `POST /api/v5/discovery/search` | Validate (400 on failure) → `resource.ToFilter()` + `resource.X` → `IDiscoveryService.Search` (eligibility auto-paging loop) → `DiscoverySearchResponseResource` (results + `poolExhausted` + requested + found). |
| `POST /api/v5/discovery/bulk-add` | Enqueue `DiscoveryBulkAddCommand` via `IManageCommandQueue.Push` (MangaBakaIds clamped to 100 — T-42-04-DOS) → `202 Accepted` immediately, no body (fire-and-forget, D-07). |

## Patterns / Conventions

- **Bare-Controller browse surface (no live-push base).** Discovery has no live entity; broadcasting here would double-fire because the add fan-out already rides `MangaController.IHandle<MangaImportedEvent>` (Pitfall 7). The acceptance gate asserts `grep -c "RestControllerWithSignalR|SignalRController" == 0`; the explanatory comment is reworded to "live-push base" so the source carries zero literal mentions (same workaround 42-02/42-03 used).
- **Validator-in-controller clamp.** The controller owns the X-clamp + enum-whitelist + range-bound BEFORE `ToFilter()` reaches `DiscoveryService.Search` — the service does NOT sanitize (42-02 handoff). `DiscoverySearchRequestValidator` is a concrete `AbstractValidator` with no ctor deps, so AutoMoqer builds it as a REAL instance in the controller fixture (the actual clamp/whitelist rules are exercised end-to-end).
- **Allow-list request DTOs.** Both the search filter and the bulk-add payload are strict allow-lists (System.Text.Json drops unlisted fields) — `MangaEditorResource` precedent.
- **Fire-and-forget bulk-add.** `POST /bulk-add` returns `TypedResults.Accepted((string?)null)` (202, no Location/body); progress is reported by the command-queue UI the FE already watches.

## Tests

`src/NzbDrone.Api.Test/Discovery/DiscoveryControllerFixture.cs` — 7 tests: route-literal pin, bare-Controller base pin, search validate→delegate→map happy path, BadRequest on out-of-range X, BadRequest on non-whitelisted enum, bulk-add enqueue + 202, >100→100 id clamp. (Controller fixtures live in Api.Test — Core.Test does not reference Mangarr.Api.V5, established convention.) The live admin-auth + XSS surface is proven end-to-end by `DiscoveryUiFixture` (Plan 42-08).

## Cross-References

- [src/NzbDrone.Core/Discovery/CLAUDE.md](../../NzbDrone.Core/Discovery/CLAUDE.md) — `IDiscoveryService` + `DiscoveryBulkAddCommand`.
- [frontend/src/Discovery/CLAUDE.md](../../../frontend/src/Discovery/CLAUDE.md) — the consumer.
- [src/Mangarr.Api.V5/CLAUDE.md](../CLAUDE.md) — the V5 API root + `[V5ApiController]` convention.
- [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42 — the Discovery vertical divergence.
