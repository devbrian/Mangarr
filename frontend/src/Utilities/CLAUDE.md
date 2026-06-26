# Utilities/

## Purpose

Pure utility functions organized by domain. Mostly media-agnostic, plus a couple of small domain-specific subfolders (`Manga/`, `Episode/`).

## Subdirectories

| Folder | Purpose |
|--------|---------|
| `Array/` | `getIndexOfFirstCharacter`, `sortByProp` |
| `Command/` | `findCommand`, `isCommandExecuting`, `isCommandComplete`, `isCommandFailed`, `isSameCommand` |
| `Constants/` | `keyCodes` |
| `Date/` | `formatDate`, `getRelativeDate`, `isToday`, `isTomorrow`, `isYesterday`, `isSameWeek`, `isInNextWeek`, `convertToTimezone`, … |
| `Episode/` | `updateEpisodes.ts` — Sonarr carry-over still present (not yet renamed to a Chapter peer) |
| `Fetch/` | `fetchJson`, `getQueryString`, `getQueryPath`, `anySignal` |
| `Filter/` | `clientSideFilterAndSort`, `findSelectedFilters`, `getFilterValue` |
| `Manga/` | `monitorOptions.ts` — manga monitor-option list (peer of Sonarr's deleted `Utilities/Series/monitorOptions`) |
| `Number/` | `formatBytes`, `formatRuntime`, `formatAge`, `formatBitrate`, `convertToBytes`, … |
| `Object/` | `isEmpty`, `hasDifferentItems`, `selectUniqueIds`, `getEntries`, `getErrorMessage` |
| `Quality/` | `getQualities` (Sonarr-shape; Phase 5 D-04 dropped from manga decision flow, helper retained for back-compat) |
| `State/` | `getNextId`, `getSectionState`, `getProviderState`, … (Redux section-state helpers) |
| `String/` | `titleCase`, `enumToTitle`, `naturalExpansion`, `combinePath`, `parseUrl`, `split`, `translate`, `isString` |
| `Table/` | `getToggledRange`, `toggleSelected` |

Note: Sonarr's `Utilities/Series/` (`getNewSeries`, `monitorOptions`, `seriesTypes`, …) was deleted in Phase 17.3 Plan 17.3-13; only `monitorOptions` survives, as the manga peer under `Utilities/Manga/`. `Utilities/Episode/updateEpisodes.ts` is a remaining Sonarr carry-over.

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

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Helpers/CLAUDE.md](../Helpers/CLAUDE.md) — Hook-based helpers (vs pure-function helpers here)
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — Components consume these utilities
