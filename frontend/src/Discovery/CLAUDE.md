# Discovery (frontend)

## Purpose

The **Discovery** browse vertical — a filtered, remote-catalog "discover + bulk-add the top N" page over the MangaBaka attribute API. The user sets attribute filters in a right-drawer, presses **Search**, gets a poster grid of titles NOT already in the library, and either bulk-adds the top N or excludes individual titles. NEW-in-Mangarr; no Sonarr peer (see [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Discovery\`

Route: `/discovery` (registered in [App/AppRoutes.tsx](../App/AppRoutes.tsx); sidebar nav entry `nav-discovery` sits between Manga and Calendar in [Components/Page/Sidebar/PageSidebar.tsx](../Components/Page/Sidebar/PageSidebar.tsx)).

## Key Files

| File | Purpose |
|------|---------|
| `Discovery.tsx` | Page shell + toolbar (Filters button, Top-X `NumberInput`, Search, Add). Renders the `useDiscoverySearch` results as a `DiscoveryCard` grid, the `poolExhausted` banner, the "Set filters and Search" empty state (manual-trigger default, D-03), and the per-card Exclude + Undo toast. Owns the `removedIds` optimistic-drop set. |
| `DiscoveryModels.ts` | TypeScript DTOs (filter, result item, genre/tag option, bulk-add payload). **Renamed from `Discovery.ts`** (a bare `Discovery.ts` would shadow the feature dir on case-insensitive imports). |
| `useDiscovery.ts` | React Query hooks: `useDiscoverySearch` (POST `/discovery/search`), `useDiscoveryBulkAdd` (fire-and-forget POST `/discovery/bulk-add`), `useDiscoveryGenres`/`useDiscoveryTags` (cached option lists), `useDiscoveryExclude` (POST `/importlistexclusion`, returns the created id) + `deleteDiscoveryExclusion(id)` (DELETE by runtime id via `fetchJson`). |
| `discoveryOptionsStore.ts` | Zustand persisted filter + X state via `createOptionsStore('discovery_options')` (DISC-09). Mirrors `addMangaOptionsStore` / `mangaOptionsStore`. |
| `discoveryPresetsStore.ts` | Zustand persisted **named saved filter presets** via `createPersist('discovery_filter_presets')` — ADDITIVE over `discoveryOptionsStore` (its own distinct localStorage key; the live `discovery_options` working filters are untouched). A preset snapshots `DiscoveryFilterState` (which already excludes `topX`). This is the bespoke-drawer divergence (Phase 42), NOT Sonarr's `Components/Filter` saved-filter FilterBuilder. |
| `FilterDrawer/PresetsRow.tsx` | Compact presets row rendered at the top of the drawer body: save current selection under a name, apply a saved preset (writes through `setDiscoveryOptions`, `topX` preserved), delete. Reuses `EnhancedSelectInput` / `TextInput` / `Button`. |
| `DiscoveryCard.tsx` | Poster-grid result card: cover (or placeholder), JSX-escaped title (no `dangerouslySetInnerHTML` — T-42-07-XSS), year/score/type badges, hover Exclude ✕. |
| `FilterDrawer/FilterDrawer.tsx` | The bespoke right-drawer query-builder. Type/Genre/Status/ContentRating tristate sections, tag typeahead, year/score ranges, sort select, Include-adult toggle. |
| `FilterDrawer/TristateChip.tsx` | The include/exclude/off chip — neutral `+` → include (green ✓) → exclude (red ✕) → neutral. Parent owns the transition; the chip renders the current visual + carries `data-state`. |
| `FilterDrawer/TagTypeahead.tsx` | Fuse.js client typeahead over the cached slim tag list + AND/OR `tag_mode` segmented toggle. Binds to integer tag ids (`tag`/`tag_not`), DISC-10. |
| `AddTopX/AddTopXModal.tsx` + `AddTopXModalContent.tsx` | Count-only "Add N Manga" bulk-add modal. Body is the factored `AddMangaFormBody` ([AddManga/AddNewManga/AddMangaFormBody.tsx](../AddManga/AddNewManga/AddMangaFormBody.tsx)) fed a count summary; submit fires `useDiscoveryBulkAdd().mutate` WITHOUT awaiting (D-07) then optimistically drops the added rows + closes. |

## Patterns / Conventions

- **Manual-trigger browse (D-03).** The page does NOT auto-fire a MangaBaka call on load — it shows the empty "Set filters and Search" state until the user presses Search. This keeps the rate-limited remote call user-driven.
- **Optimistic-drop set.** A local `removedIds` `Set` filters the volatile `useDiscoverySearch` results so added/excluded rows drop without touching the `['/manga']` cache (no SignalR double-removal, D-08).
- **Fire-and-forget bulk add (D-07).** The modal kicks the bulk-add mutation, closes immediately, and drops the rows; progress is reported by the command-queue UI the FE already watches. The server returns 202.
- **Global exclusion reuse (D-05).** Per-card Exclude writes the GLOBAL `ImportListExclusion` via the existing `/api/v5/importlistexclusion` surface (blocks all import lists) — NOT a Discovery-only hidden list. The Undo toast mitigates accidental excludes with an immediate DELETE; on POST failure the card is re-inserted (self-healing).
- **Zero new form vocabulary.** The bulk-add modal reuses `AddMangaFormBody` (factored out of `AddNewMangaModalContent`); both modals read `addMangaOptionsStore` independently.

## FilterModal Divergence (NEW-in-Mangarr)

Discovery's filter UI is a **bespoke right-drawer with tristate include/exclude/off chips and a remote-API tag typeahead, NOT the Sonarr-canonical `FilterModal`/`FilterBuilder`** (`Components/Filter/`, `Helpers/Props/filterBuilderTypes.ts`). Sonarr's filter builder targets locally-held collections with saved named filters; Discovery builds a query against MangaBaka's remote attribute search where include/exclude pairs (`genre`/`genre_not`) and a ~2,692-row tag typeahead have no `FilterBuilder` analog. **Sub-components (`EnhancedSelectInput`, `NumberInput`, `CheckInput`, form shells) are reused; the container diverges.** Preserve Sonarr's shape except where the manga/remote-browse domain forces us. Full rationale in [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42.

## `data-testid` selectors (Playwright E2E)

The Discovery automation fixture ([src/NzbDrone.Automation.Test/Discovery/DiscoveryUiFixture.cs](../../../src/NzbDrone.Automation.Test/Discovery/DiscoveryUiFixture.cs)) drives these:

| Selector | Element |
|----------|---------|
| `nav-discovery` | Sidebar entry → `/discovery` |
| `discovery-page` / `discovery-empty-state` / `discovery-grid` | Toolbar / empty state / results grid |
| `discovery-filters-button` / `discovery-filter-drawer` | Filters toggle / right-drawer |
| `discovery-chip-{value}` (carries `data-state`) | Tristate chip (Type/Genre/Status/ContentRating) |
| `discovery-tag-search` / `discovery-tag-suggestions` / `discovery-tagmode-and\|or` | Tag typeahead |
| `discovery-include-adult` | Include-adult toggle |
| `discovery-topx-input` / `discovery-search-button` / `discovery-add-button` | Top-X / Search / Add |
| `discovery-card-{id}` / `discovery-card-exclude-{id}` | Result card / Exclude ✕ |
| `discovery-pool-exhausted` / `discovery-undo-toast` / `discovery-undo-button` | Pool-exhausted banner / Undo toast |
| `add-top-x-modal` / `add-top-x-modal-add-button` | Count-only Add modal / submit |
| `discovery-year-lower` | Year-range "From" `NumberInput` (drives the clamp-on-blur-only proof) |
| `discovery-presets-row` | Saved-presets row container (top of the drawer body) |
| `discovery-preset-select` / `discovery-preset-apply` / `discovery-preset-delete` | Saved-preset picker (`EnhancedSelectInput` wrapper) / Apply / Delete |
| `discovery-preset-name-input` / `discovery-preset-save` | New-preset name `TextInput` / Save |
| `discovery-preset-empty` | Empty-state line shown when no presets are saved |

## Cross-References

- [src/Mangarr.Api.V5/Discovery/CLAUDE.md](../../../src/Mangarr.Api.V5/Discovery/CLAUDE.md) — the 4 endpoints this consumes.
- [src/NzbDrone.Core/Discovery/CLAUDE.md](../../../src/NzbDrone.Core/Discovery/CLAUDE.md) — the eligibility loop + MangaBaka browse.
- [AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — the shared `AddMangaFormBody` + `addMangaOptionsStore`.
- [DIVERGENCE.md](../../../DIVERGENCE.md) Phase 42 — FilterModal divergence rationale.
