# Utilities/

## Purpose

Pure utility functions organized by domain. Everything here is **media-agnostic** post-Phase-17.3 — the `Episode/` and `Series/` TV-shape subfolders were deleted in Plan 17.3-13 atomic stub-dir delete alongside the parent stub directories.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Utilities\`

**File count**: ~50 files across 13 sub-domains (post-Plan-17.3-13 — Series/ + Episode/ subfolders retired).

## Subdirectories

| Folder | Purpose | Reusable? |
|--------|---------|-----------|
| `Array/` | `getIndexOfFirstCharacter`, `sortByProp`, … | ✓ |
| `Command/` | `findCommand`, `isCommandExecuting`, `isCommandFinished` | ✓ |
| `Constants/` | Key codes, magic numbers | ✓ |
| `Date/` | `formatDate`, `getRelativeDate`, `isToday`, `isTomorrow`, `isYesterday`, `isThisWeek`, … | ✓ |
| `Fetch/` | `fetchJson`, `getQueryString`, `getQueryPath` | ✓ |
| `Filter/` | `clientSideFilterAndSort`, `findSelectedFilters` | ✓ |
| `Number/` | `formatBytes`, `formatRuntime`, `formatAge` | ✓ |
| `Object/` | `isEmpty`, `hasDifferentItems`, `selectUniqueIds` | ✓ |
| `Quality/` | `getQualities` (Sonarr-shape; Phase 5 D-04 dropped from manga decision flow but helper retained for TV-shape consumers) | ✓ (back-compat) |
| `String/` | `titleCase`, `enumToTitle`, `naturalExpansion` | ✓ |
| `Table/` | `getToggledRange`, `toggleSelected` | ✓ |
| `State/` | `getNextId` | ✓ |

Note: Sonarr `Utilities/Episode/` (1 file — `updateEpisodes.ts`) + `Utilities/Series/` (4 files — `getNewSeries`, `monitorOptions`, `seriesTypes`, …) were Phase 15 Plan 15-12 stubs and were deleted in Plan 17.3-13 D-09/D-10 atomic stub-dir delete. The widening callsites (`getProgressBarKind`, `getSeriesStatusDetails`) that depended on them were inlined into the per-component forks per Plan 17.3-08 D-14.

## Top-Level Helpers

| File | Purpose |
|------|---------|
| `getPathWithUrlBase.ts` | Prefix path with `urlBase` for reverse-proxy support |
| `ResolutionUtility.ts` | Quality-resolution helpers |
| `browser.ts` | Browser/OS detection |
| `getUniqueElementId.ts` | DOM unique IDs for accessibility |
| `scrollLock.ts` | Prevent body scroll when modal open |
| `SignalRLogger.ts` | Logging wrapper for SignalR client |

## Common Patterns

### Date formatting
```typescript
import { formatDate, getRelativeDate, isToday } from 'Utilities/Date/...';
formatDate(date, 'YYYY-MM-DD');     // → "2026-04-30"
getRelativeDate(date);              // → "in 3 days"
isToday(airDate);                   // → boolean
```

### Filter + sort
```typescript
import clientSideFilterAndSort from 'Utilities/Filter/clientSideFilterAndSort';
const visible = clientSideFilterAndSort(items, filters, sortKey, sortDir);
```

### Number formatting
```typescript
import formatBytes from 'Utilities/Number/formatBytes';
formatBytes(1024 * 1024 * 5);       // → "5.0 MB"
```

## Manga Adaptation Notes (Phase 17.3 close-out)

### `Series/` — **Done — deleted** (Plan 17.3-13)
The Sonarr `Utilities/Series/` subfolder (`getNewSeries`, `monitorOptions`,
`seriesTypes`, …) was a Phase 15 Plan 15-12 stub. Plan 17.3-13 atomic
stub-dir delete retired it alongside the parent `frontend/src/Series/`
subtree. Manga peers live inline in the canonical feature modules:
- `monitorOptions` — manga 5-value `MangaMonitor` literal in `Manga/Manga.ts` (per Phase 6 D-03)
- `seriesTypes` — no peer; PROJECT.md Out-of-Scope (manga has no series-type concept)
- `getNewSeries` — manga peer in `AddManga/useAddManga.ts` (`useAddManga()` mutation)

### `Episode/` — **Done — deleted** (Plan 17.3-13)
The Sonarr `Utilities/Episode/` subfolder (1 file: `updateEpisodes.ts`)
was a Phase 15 Plan 15-12 stub. Deleted in Plan 17.3-13. Manga peer:
`useToggleChapterMonitored` / `useBulkToggleChaptersMonitored` in
`Chapter/useChapter.ts`.

### `Quality/`
Phase 5 D-04 dropped Quality from the manga decision flow; `getQualities`
helper retained for back-compat with TV-shape consumers.

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Helpers/CLAUDE.md](../Helpers/CLAUDE.md) — Hook-based helpers (vs pure-function helpers here)
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — Components consume these utilities
