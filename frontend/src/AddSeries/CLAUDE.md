# AddSeries/

## Purpose

Two workflows for getting series into the library:
1. **Add New** — Search a metadata provider for a title, configure monitoring/quality, save
2. **Import Existing** — Pick a folder; backend lists subfolders; user maps each to a metadata match

Will be **renamed to AddManga** in the Mangarr migration.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\AddSeries\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `AddSeries.ts` | Type definitions for add flow (`AddSeriesOptions`, etc.) |
| `addSeriesOptionsStore.ts` | Zustand — last-used options (root folder, quality profile, monitoring, etc.) persisted |
| `SeriesMonitoringOptionsPopoverContent.tsx` | Help text for monitor presets |
| `SeriesMonitorNewItemsOptionsPopoverContent.tsx` | Help text for "monitor new items" option |
| `SeriesTypePopoverContent.tsx` | Help text for series-type picker |

## Subdirectories

### `AddNewSeries/` — Search + Add
| File | Purpose |
|------|---------|
| `AddNewSeries.tsx` | Page; search input + results list |
| `AddNewSeriesSearchResult.tsx` | One result row in the search list |
| `AddNewSeriesModal.tsx` / `AddNewSeriesModalContent.tsx` | Configure & confirm modal (root folder, quality, monitoring) |
| `useAddSeries.ts` | Mutation hook |

### `ImportSeries/` — Import Existing Folder
| File / Subdir | Purpose |
|---------------|---------|
| `ImportSeriesPage.tsx` | Page wrapper with two stages |
| `SelectFolder/ImportSeriesSelectFolder.tsx` | Stage 1: pick a root folder (lists existing or browse) |
| `Import/ImportSeries.tsx` | Stage 2: list subfolders; map each to series |
| `Import/ImportSeriesTable.tsx` | Table of folder → series mapping |
| `Import/ImportSeriesRow.tsx` | One row |
| `Import/SelectSeries/ImportSeriesSelectSeries.tsx` | Per-row series picker (search-as-you-type) |
| `Import/SelectSeries/ImportSeriesSearchResult.tsx` | Search result row |
| `Import/importSeriesStore.ts` | Zustand state for the import session |
| `Import/useImportSeries.ts` | Mutation |

## Add-New Flow

```
User types title → Frontend POST /api/v5/series/lookup?term=
        ↓ List<SeriesResource>
User clicks a result → Modal opens with form
        ↓ User picks rootFolder, qualityProfile, monitor preset, tags
User clicks "Add" → POST /api/v5/series body { … }
        ↓ Backend AddSeriesService persists + queues refresh
        ↓ SignalR pushes seriesAdded → list updates
Navigate to /series/:titleSlug
```

## Import Flow

```
User picks rootFolder → Backend lists immediate subfolders
        ↓ List<UnmappedFolder>
For each subfolder, run lookup against folder name
        ↓ Suggested matches (best top, others below)
User confirms each row (or skips) → POST /api/v5/series/import
        ↓ Backend creates Series rows linked to folders
```

## Manga Adaptation Plan

### Renames
| File | Manga |
|------|-------|
| `AddSeries.ts` | `AddManga.ts` |
| `addSeriesOptionsStore.ts` | `addMangaOptionsStore.ts` |
| `SeriesMonitoringOptionsPopoverContent.tsx` | `MangaMonitoringOptionsPopoverContent.tsx` |
| `SeriesTypePopoverContent.tsx` | `MangaTypePopoverContent.tsx` (Manga / Manhwa / Manhua / OEL) |
| `AddNewSeries/` | `AddNewManga/` |
| `ImportSeries/` | `ImportManga/` |

### Type Changes
- `AddSeriesOptions.monitor` — preset list changes:
  - Remove: `pilot`, `firstSeason`, `lastSeason`, `monitorSpecials`, `unmonitorSpecials`
  - Keep: `all`, `future`, `missing`, `existing`, `recent`, `none`
  - Add: `firstChapter`, `latestChapter`, `latestVolume`
- Form picks: replace `seriesType` with `mangaType`

### API Endpoint Changes
- `/api/v5/series/lookup` → `/api/v5/manga/lookup` (or alias temporarily)
- Backend lookup hits MangaDex / AniList instead of TVDB

### UI Copy
- "Add Series" → "Add Manga"
- "Import Existing Series" → "Import Existing Manga"
- "Series found" → "Manga found"

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Where added series end up
- [../RootFolder/](../RootFolder/) — Root folder picker reused
- [../../../src/NzbDrone.Core/Tv/CLAUDE.md](../../../src/NzbDrone.Core/Tv/CLAUDE.md) — Backend `AddSeriesService`
- [../../../src/NzbDrone.Core/MetadataSource/CLAUDE.md](../../../src/NzbDrone.Core/MetadataSource/CLAUDE.md) — Lookup source
