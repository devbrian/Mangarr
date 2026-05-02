# Utilities/

## Purpose

Pure utility functions organized by domain. Almost everything here is **media-agnostic** and reusable for Mangarr — except for `Episode/` and `Series/` subfolders, which contain TV-specific helpers.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Utilities\`

**File count**: ~50+ files across 15 sub-domains.

## Subdirectories

| Folder | Purpose | Reusable? |
|--------|---------|-----------|
| `Array/` | `getIndexOfFirstCharacter`, `sortByProp`, … | ✓ |
| `Command/` | `findCommand`, `isCommandExecuting`, `isCommandFinished` | ✓ |
| `Constants/` | Key codes, magic numbers | ✓ |
| `Date/` | `formatDate`, `getRelativeDate`, `isToday`, `isTomorrow`, `isYesterday`, `isThisWeek`, … | ✓ |
| **`Episode/`** | `updateEpisodes` | **Needs Manga update** |
| `Fetch/` | `fetchJson`, `getQueryString`, `getQueryPath` | ✓ |
| `Filter/` | `clientSideFilterAndSort`, `findSelectedFilters` | ✓ |
| `Number/` | `formatBytes`, `formatRuntime`, `formatAge` | ✓ |
| `Object/` | `isEmpty`, `hasDifferentItems`, `selectUniqueIds` | ✓ |
| `Quality/` | `getQualities` | Adapt for manga qualities |
| **`Series/`** | `getNewSeries`, `monitorOptions`, `seriesTypes` | **Needs Manga update** |
| `String/` | `titleCase`, `enumToTitle`, `naturalExpansion` | ✓ |
| `Table/` | `getToggledRange`, `toggleSelected` | ✓ |
| `State/` | `getNextId` | ✓ |

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

## Manga Adaptation Notes

### `Series/`
Contains TV-specific helpers (`monitorOptions`, `seriesTypes`). To migrate:
- Rename folder to `Manga/`
- `monitorOptions` — drop `pilot`, `firstSeason`, `lastSeason`, `monitorSpecials`, `unmonitorSpecials`; add `firstChapter`, `latestChapter`, `latestVolume`
- `seriesTypes` → `mangaTypes` (manga / manhwa / manhua / OEL)

### `Episode/`
- Rename to `Chapter/`
- `updateEpisodes` → `updateChapters`

### `Quality/`
Update `getQualities` lookup once Quality enum is updated for manga tiers.

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Helpers/CLAUDE.md](../Helpers/CLAUDE.md) — Hook-based helpers (vs pure-function helpers here)
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — Components consume these utilities
