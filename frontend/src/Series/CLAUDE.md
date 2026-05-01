# Series Feature Module

## Purpose

The main feature module for managing TV series. This will be **renamed to Manga** for the Mangarr adaptation.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Series\`

## Directory Structure

```
Series/
├── Series.ts                    # Type definitions
├── useSeries.ts                 # API hooks (largest file)
├── seriesOptionsStore.ts        # View preferences (Zustand)
├── Index/                       # List view
│   ├── SeriesIndex.tsx          # Main list page (1000+ lines)
│   ├── SeriesIndexFilterMenu.tsx
│   ├── SeriesIndexSortMenu.tsx
│   ├── Posters/                 # Poster grid view
│   │   ├── SeriesIndexPosters.tsx
│   │   ├── SeriesIndexPoster.tsx
│   │   └── SeriesIndexPosterInfo.tsx
│   ├── Overview/                # Overview list view
│   │   ├── SeriesIndexOverviews.tsx
│   │   └── SeriesIndexOverview.tsx
│   ├── Table/                   # Table view
│   │   ├── SeriesIndexTable.tsx
│   │   └── SeriesIndexRow.tsx
│   └── ProgressBar/             # Progress components
├── Details/                     # Detail view
│   ├── SeriesDetails.tsx        # Main detail page (900+ lines)
│   ├── SeriesDetailsSeason.tsx
│   └── SeriesDetailsLinks.tsx
├── Edit/                        # Edit functionality
│   ├── EditSeriesModal.tsx
│   └── EditSeriesModalContent.tsx
├── Delete/                      # Delete functionality
│   └── DeleteSeriesModal.tsx
├── History/                     # Series history
│   └── SeriesHistoryRow.tsx
├── Search/                      # Search actions
├── MonitoringOptions/           # Monitor settings
└── MoveSeries/                  # Move series
```

## Key Types (`Series.ts`)

```typescript
interface Series {
  id: number;
  title: string;
  sortTitle: string;
  status: SeriesStatus;       // 'continuing' | 'ended' | 'upcoming'
  seriesType: SeriesType;     // 'anime' | 'daily' | 'standard'

  // External IDs
  tvdbId: number;
  imdbId?: string;
  tvMazeId?: number;

  // Organization
  path: string;
  rootFolderPath: string;
  qualityProfileId: number;
  tags: number[];

  // Monitoring
  monitored: boolean;
  monitorNewItems: MonitorNewItems;

  // Content
  seasons: Season[];
  genres: string[];
  ratings: Ratings;

  // Statistics
  statistics: SeriesStatistics;
  // episodeCount, episodeFileCount, sizeOnDisk, etc.
}

type SeriesMonitor =
  | 'all'
  | 'future'
  | 'missing'
  | 'existing'
  | 'recent'
  | 'pilot'
  | 'firstSeason'
  | 'lastSeason'
  | 'monitorSpecials'
  | 'unmonitorSpecials'
  | 'none';
```

## API Hooks (`useSeries.ts`)

### Read Operations
```typescript
// Fetch all series
const { items, isLoading } = useSeries();

// Get single series from cache
const series = useSingleSeries(id);

// Filtered/sorted list for index view
const { items, filters, ... } = useSeriesIndex();
```

### Write Operations
```typescript
// Save series changes
const { mutate: saveSeries } = useSaveSeries(moveFiles);
saveSeries(series);

// Delete series
const { mutate: deleteSeries } = useDeleteSeries(id, options);

// Toggle monitored status
const { mutate: toggleMonitored } = useToggleSeriesMonitored(id);

// Bulk operations
const { mutate: bulkEdit } = useSaveSeriesEditor();
const { mutate: bulkDelete } = useBulkDeleteSeries();
```

### Filtering & Sorting
```typescript
// Predefined filters
const FILTERS = [
  { key: 'all', label: 'All' },
  { key: 'monitored', label: 'Monitored Only' },
  { key: 'continuing', label: 'Continuing Only' },
  // ...
];

// Filter predicates
const FILTER_PREDICATES = {
  missing: (item) => item.statistics.episodeCount > item.statistics.episodeFileCount,
  ended: (item) => item.status === 'ended',
  // ~15 more predicates
};

// Sort predicates
const SORT_PREDICATES = {
  status: (item) => item.status,
  nextAiring: (item) => item.nextAiring,
  sizeOnDisk: (item) => item.statistics.sizeOnDisk,
  // ...
};
```

## Options Store (`seriesOptionsStore.ts`)

Zustand store for view preferences, persisted to localStorage:

```typescript
const useSeriesOptions = create(
  persist(
    (set, get) => ({
      // View mode
      view: 'posters' | 'overview' | 'table',

      // Filter/sort
      selectedFilterKey: 'all',
      sortKey: 'sortTitle',
      sortDirection: 'ascending',

      // Poster options
      posterOptions: {
        size: 250,
        showTitle: true,
        showMonitored: true,
        showQualityProfile: true,
        // ...
      },

      // Table columns
      tableColumns: [...],
    }),
    { name: 'series_options' }
  )
);
```

## Views

### Index View (`SeriesIndex.tsx`)
- Three display modes: Posters, Overview, Table
- Toolbar with filter, sort, view toggle
- Jump bar for alphabetical navigation
- Select mode for bulk operations
- Footer with actions for selected items

### Poster View (`SeriesIndexPoster.tsx`)
- Grid layout with responsive sizing
- Cover image with hover overlay
- Progress bar showing episode completion
- Action buttons: Edit, Delete, Refresh, Search

### Detail View (`SeriesDetails.tsx`)
- Fanart background
- Series metadata display
- Season list with expandable sections
- Episode list within each season
- Action buttons: Refresh, Search, Edit, Delete

## Commands

```typescript
// Execute commands via API
executeCommand({
  name: CommandNames.RefreshSeries,
  seriesIds: [seriesId]
});

executeCommand({
  name: CommandNames.SeriesSearch,
  seriesId: seriesId
});
```

## Manga Adaptation Plan

### Rename Files
| Current | New |
|---------|-----|
| `Series.ts` | `Manga.ts` |
| `useSeries.ts` | `useManga.ts` |
| `seriesOptionsStore.ts` | `mangaOptionsStore.ts` |
| `SeriesIndex.tsx` | `MangaIndex.tsx` |
| `SeriesDetails.tsx` | `MangaDetails.tsx` |

### Type Changes
```typescript
// Series → Manga
interface Manga {
  // Remove TV-specific
  // tvdbId, imdbId, tvMazeId, network, airTime

  // Add manga-specific
  mangadexId?: string;
  anilistId?: number;
  malId?: number;
  author?: string;
  artist?: string;
  originalLanguage?: string;

  // seasons → volumes (optional)
  volumes?: Volume[];

  // Update terminology
  // seriesType → mangaType ('manga' | 'manhwa' | 'manhua')
}
```

### UI Changes
- Episode → Chapter
- Season → Volume
- "Air Date" → "Release Date"
- Quality meanings (1080p → high-res scans)

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Episode/CLAUDE.md](../Episode/CLAUDE.md) - Episode components
- [PROJECT_CONTEXT.md](../../../PROJECT_CONTEXT.md) - Architecture
- [NzbDrone.Core/Tv/CLAUDE.md](../../../src/NzbDrone.Core/CLAUDE.md) - Backend models
