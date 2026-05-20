# Mangarr.Api.V5/ImportLists

## Purpose

V5 REST surface for the **ImportList substrate** (Phase 26 IL-05). Wraps the
`NzbDrone.Core.ImportLists` substrate (Plan 26-04) and the `Exclusions/` subdomain
in V5-canonical `ProviderControllerBase` + `RestController` controllers consumed by
the Settings → ImportLists FE page.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Mangarr.Api.V5\ImportLists\`

## Endpoint Surface

Routes are derived by ASP.NET Core from the `[V5ApiController]` attribute +
controller class name (case-insensitive routing — both `importlist` and
`ImportList` resolve).

### `/api/v5/importlist` (ImportListController)
Inherits **all 10 endpoints** from `Mangarr.Api.V5.Provider.ProviderControllerBase`
(no overrides; Phase 26 substrate adds ZERO field-level SharedValidator rules per
D-08 — Phase 27 providers add per-Settings rules).

| Method | Route | Behavior |
|--------|-------|----------|
| GET    | `/api/v5/importlist`                | List all definitions |
| GET    | `/api/v5/importlist/{id}`           | Get by id |
| POST   | `/api/v5/importlist`                | Create (test if `Enable && !skipTesting`) |
| PUT    | `/api/v5/importlist/{id}`           | Update (test if changed + enabled) |
| DELETE | `/api/v5/importlist/{id}`           | Delete |
| GET    | `/api/v5/importlist/schema`         | Provider templates (returns `[]` until Phase 27 per D-08) |
| POST   | `/api/v5/importlist/test`           | Test a definition |
| POST   | `/api/v5/importlist/testall`        | Test all enabled |
| PUT    | `/api/v5/importlist/bulk`           | Bulk-update enabled / root / profile fields |
| DELETE | `/api/v5/importlist/bulk`           | Bulk-delete by Ids |

### `/api/v5/importlistexclusion` (ImportListExclusionController)
Standard `RestController<TResource>` shape (NOT a ProviderControllerBase subclass —
exclusions are static entities, not ThingiProvider plugins).

| Method | Route | Behavior |
|--------|-------|----------|
| GET    | `/api/v5/importlistexclusion`         | Paged list (sort keys: id, title, mangaDexId) |
| GET    | `/api/v5/importlistexclusion/{id}`    | Get by id |
| POST   | `/api/v5/importlistexclusion`         | Create (MangaDexId uniqueness checked) |
| PUT    | `/api/v5/importlistexclusion/{id}`    | Update |
| DELETE | `/api/v5/importlistexclusion/{id}`    | Delete |
| DELETE | `/api/v5/importlistexclusion/bulk`    | Bulk-delete by Ids |

## Key Files

| File | Purpose |
|------|---------|
| `ImportListController.cs` | V5 provider CRUD — **D-14 authored from `src/Mangarr.Api.V5/Indexers/IndexerController.cs:11` 17-line template** (main controller NOT preserved in `.planning/reference/sonarr-vertical-slices/import-lists/v5-controller/`) |
| `ImportListResource.cs` | FE-facing DTO; subclasses `ProviderResource<T>`. Field set drawn from `ImportListDefinition` (Plan 26-04). |
| `ImportListResourceMapper.cs` | Round-trips Resource ↔ Definition. Inherits Settings/Tags/Fields handling from base. |
| `ImportListBulkResource.cs` | Bulk-payload DTO; nullable fields for partial updates. |
| `ImportListBulkResourceMapper.cs` | `?? existing` short-circuit on each nullable field. |
| `ImportListExclusionController.cs` | Verbatim port from ref slice with TvdbId → MangaDexId rule swap. |
| `ImportListExclusionResource.cs` | Manga-ID triplet: `MangaDexId` (string) + `MalId` (int?) + `AniListId` (int?) + `Title`. Includes static `ImportListExclusionResourceMapper` (model ↔ resource round-trip). |
| `ImportListExclusionBulkResource.cs` | `HashSet<int> Ids` payload for bulk-delete. |
| `ImportListExclusionExistsValidator.cs` | FluentValidation `PropertyValidator` enforcing MangaDexId uniqueness (NULL-tolerant per Migration 003 UNIQUE-with-NULLs semantics). |
| `CLAUDE.md` | This file. |

## Patterns / Conventions

- **`ProviderControllerBase<TR, TBR, TP, TPD>` inheritance** (10-endpoint provider CRUD surface). Substrate adds ZERO `SharedValidator` rules per D-08 — Phase 27 providers add rules in their own POCO classes.
- **`RestController<T>` for static entities** (Exclusion). Standard CRUD + paged GET via `MapToPagingSpec` + `ApplyToPage` helpers.
- **`FluentValidation.PropertyValidator` for uniqueness checks** (Exclusion). NULL-tolerant — secondary-ID-only exclusions (AniList-only / MAL-only) ride a NULL MangaDexId per Migration 003 SQLite UNIQUE-with-NULLs semantics (RESEARCH §Q4).
- **T-26-05-02 mitigation**: substrate Resource POCOs surface NO AccessToken/RefreshToken fields; Phase 27 provider Settings POCOs apply `[FieldDefinition(Privacy = Password, Hidden = HiddenType.Hidden)]` at the field level via `SchemaBuilder` reflection.

## Manga Adaptation Notes

| Sonarr peer | Mangarr peer | Reason |
|-------------|--------------|--------|
| `TvdbId` (int, single) on `ImportListExclusionResource` | `MangaDexId` (string) + `MalId` (int?) + `AniListId` (int?) | Manga has 3 canonical metadata sources, not 1; per Migration 003 |
| `QualityProfileId` on `ImportListResource` | `TranslationProfileId` + `CustomFormatProfileId` | Phase 5 D-04 — translation language replaces video-quality model |
| `SeasonFolder` / `SeriesType` | DROPPED | Manga has no Season; Migration 001 stripped both columns |
| `SearchForMissingEpisodes` | `SearchForMissingChapters` | Naming peer |
| `IImportList` | `IMangaImportList` | Naming peer; Plan 26-04 IL-02 |
| Sonarr "Sync Now" button (per provider) | NONE | D-05 — Sonarr-canonical Settings page has no Sync-Now / TestAll button (users trigger via System → Tasks UI or `POST /api/v5/command {name:"ImportListSync"}`) |

## Cross-References

- [ProviderControllerBase.cs:19](../Provider/ProviderControllerBase.cs) — inherited CRUD + schema + test + bulk surface
- [IndexerController.cs:11](../Indexers/IndexerController.cs) — D-14 in-repo template provenance for `ImportListController`
- [Plan 26-04 substrate — `src/NzbDrone.Core/ImportLists/`](../../NzbDrone.Core/ImportLists/) — `ImportListDefinition`, `IMangaImportList`, `IImportListFactory`, `Exclusions/`
- [Phase 26 CONTEXT.md](../../../.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-CONTEXT.md) — D-05 (no Sync-Now), D-08 (Phase 27 owns providers), D-14 (in-repo template)
- [Phase 26 RESEARCH.md §Q5](../../../.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-RESEARCH.md) — verified 10-endpoint inherited surface
- [Plan 26-06](../../../.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-06-PLAN.md) — automation fixtures for empty-state SC#6
