# Wanted/

## Purpose

Two views into "what's missing or upgradable":

- **Missing** — episodes that should exist (monitored + aired) but have no file
- **CutoffUnmet** — episodes that have a file, but the file is below the quality profile's cutoff (i.e., upgradable)

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Wanted\`

## Subdirectories

### Missing/ (~7 files)
| File | Purpose |
|------|---------|
| `Missing.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10) |
| `MangaMissing.tsx` | Phase 7 thin wrapper rendering `<Missing mediaType="manga" />` (Plan 07-10) |
| `MissingRow.tsx` | One missing episode row |
| `MissingFilterModal.tsx` | Filter |
| `missingOptionsStore.ts` | Zustand options |
| `useMissing.tsx` | API hook (accepts `mediaType` arg switching `/wanted/missing` ↔ `/manga/wanted/missing`) |

### CutoffUnmet/ (~6 files)
| File | Purpose |
|------|---------|
| `CutoffUnmet.tsx` | Page (accepts `mediaType?: 'series' \| 'manga'` prop — Phase 7 D-10 / Plan 12-08) |
| `MangaCutoffUnmet.tsx` | Plan 12-08 thin wrapper rendering `<CutoffUnmet mediaType="manga" />` |
| `CutoffUnmetRow.tsx` | Row |
| `CutoffUnmetFilterModal.tsx` | Filter modal (Phase 12 REVIEW MED-03 follow-up — mirrors `MissingFilterModal.tsx` shape; consumes `FILTER_BUILDER` export from `useCutoffUnmet.tsx` and dispatches `setCutoffUnmetOption('selectedFilterKey', ...)` against `cutoffUnmetOptionsStore`; `customFilterType="wanted.cutoffUnmet"` matches the existing `WANTED_CUTOFF_UNMET` entity key in `Episode/episodeEntities.ts`) |
| `cutoffUnmetOptionsStore.ts` | Zustand |
| `useCutoffUnmet.tsx` | Hook (accepts `mediaType` arg switching `/wanted/cutoff` ↔ `/manga/wanted/cutoff`; exports `FILTERS` + `FILTER_BUILDER`) |

## API

- `GET /api/v5/wanted/missing` → server-paged list (TV)
- `GET /api/v5/manga/wanted/missing` → server-paged list (manga, Phase 6 endpoint)
- `GET /api/v5/wanted/cutoff` → server-paged list

Per-row actions:
- "Search" → executes `EpisodeSearch` command
- "Toggle Monitor" → PUT `/api/v5/episode/{id}` with `{monitored: false}`

Bulk actions (footer):
- Search all
- Toggle monitor
- Remove (set unmonitored — not delete)

## Manga Adaptation Notes

These pages are **conceptually identical** for manga: just rename the data type.

- `Missing` → "missing chapters" — chapters that should exist but no file
- `CutoffUnmet` → "below cutoff chapters" — files exist but quality profile isn't satisfied

### Key Considerations
- Manga has chapter scheduling that's less precise than TV airing — backend logic for "should exist by now" needs adaptation:
  - Sonarr: airDateUtc < now → missing
  - Mangarr: lastKnownChapterReleaseDate < now → missing? Or just any monitored chapter without a file?
- "Cutoff" semantics carry over (high-res scan vs. low-res; official vs. fan; etc.)

### File Renames
| Sonarr | Manga |
|--------|-------|
| `MissingRow.tsx` (Episode columns) | Update to Chapter columns |
| `useMissing.tsx` (returns Episode rows) | `useMissingChapters.tsx` |

## Phase 7 D-10 + Lock #1 — mediaType discriminator (Plan 07-10)

Per Phase 7 D-10 (existing tables/pages drive off API URL paths; manga-mode is selected via
route + query-key namespace) and RESEARCH Lock #1 (separate top-level routes, NOT query-string
or fork), Plan 07-10 added — mirroring the Activity Plan 07-09 pattern verbatim:

**Hook (extended in place — `useMissing.tsx`):**
Accepts a `mediaType: 'series' | 'manga'` arg defaulting to `'series'`. The arg switches the
`path` passed to `usePagedApiQuery` between `/wanted/missing` ↔ `/manga/wanted/missing`. React
Query's queryKey is auto-derived from the `path` arg, giving clean cache namespacing
(`['/wanted/missing']` vs `['/manga/wanted/missing']`) — Pitfall 5 cache no-collision. Exports
`MissingMediaType` type alias for downstream consumers.

**Page (extended in place — `Missing.tsx`):**
Accepts `mediaType?: 'series' | 'manga'` prop defaulting to `'series'`. The prop is forwarded
to `useMissing()` and to `MissingContent`. Empty-state copy switches to manga-specific i18n
key (`NothingWanted`) when `mediaType === 'manga'`. Bulk-toggle invalidation queryKey
(`useToggleEpisodesMonitored([...])`) likewise switches between the two paths so monitor
toggles invalidate the correct cache.

**Thin wrapper (NEW — Lock #13 Option B per RESEARCH):**

| Wrapper file | Renders | Mounted at route |
|--------------|---------|------------------|
| `Missing/MangaMissing.tsx` | `<Missing mediaType="manga" />` | `/manga/wanted/missing` |

**SignalR auto-refresh:** Plan 07-02's `frontend/src/Components/SignalRListener.tsx` handler
for `manga/wanted/missing` invalidates the matching React Query key
(`['/manga/wanted/missing']`) — the manga Wanted/Missing page auto-refreshes on backend events
identically to how the TV Wanted/Missing page auto-refreshes on `wanted/missing` SignalR pushes.

**Phase 8 cleanup:** When `/manga/wanted/missing` is promoted (or `/wanted/missing` is dropped),
the thin wrapper merges into `Missing.tsx` (`mediaType` default flips to `'manga'`) and the
wrapper is deleted. The hook loses the discriminator (single URL).

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Activity/CLAUDE.md](../Activity/CLAUDE.md) — Plan 07-09 mediaType pattern (copy-target)
- [../Episode/CLAUDE.md](../Episode/CLAUDE.md) — Episode types used
- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Each row links back to series
- [../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md](../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md) — Cutoff logic lives in `CutoffSpecification`
