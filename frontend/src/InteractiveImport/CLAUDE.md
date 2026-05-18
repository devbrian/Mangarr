# InteractiveImport/

## Purpose

**Manual import** — user picks a folder of files, sees a per-file table with parsed series/season/episode/quality/language guesses, manually corrects mappings, and imports. Used when:
- Auto-import failed
- Files exist outside the watched download folder
- User wants to bulk-import an existing library

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\InteractiveImport\`

**File count**: ~48 files.

## Top-Level Files

| File | Purpose |
|------|---------|
| `InteractiveImportModal.tsx` | Modal entry point |
| `InteractiveImport.ts` | Type definitions |
| `ImportMode.ts` | enum: `move` / `copy` / `auto` |
| `ReleaseType.ts` | enum: `singleEpisode` / `multiEpisode` / `seasonPack` |
| `interactiveImportFoldersStore.ts` | Zustand: recently used folders |
| `interactiveImportOptionsStore.ts` | Zustand: import options |
| `useInteractiveImport.ts` | Mutation hook |

## Subdirectory: Interactive/
| File | Purpose |
|------|---------|
| `InteractiveImportModalContent.tsx` | Main table with all rows |
| `InteractiveImportRow.tsx` | One file row |
| `InteractiveImportRowCellPlaceholder.tsx` | Placeholder/loading cells |

## Per-Cell Selection Modals (one subdir per editable cell)

| Subdir | Modal | Purpose |
|--------|-------|---------|
| `Folder/` | `InteractiveImportSelectFolderModalContent`, `FavoriteFolderRow`, `RecentFolderRow` | Pick the source folder |
| `Chapter/` | `SelectChapterModal`, `SelectChapterModalContent` | Pick chapter(s) — Plan 25-04 Task 1 renamed `Episode/` → `Chapter/` per Pitfall 13 atomic-per-decision commit; stub body preserved per Plan 15-12 |
| `Manga/` | `SelectMangaModal` | Pick or correct manga — Plan 25-04 Task 2 renamed `Series/` → `Manga/` per Pitfall 13; stub body preserved per Plan 15-12 |
| `Season/` | (deleted Plan 25-04 Task 3 — manga has no season per DOMAIN-02; SelectSeasonModal stub + per-row season cell + season-modal trigger removed) | n/a |
| `Language/` | `SelectLanguageModal`, `SelectLanguageModalContent` | Set language(s) |
| `Quality/` | `SelectQualityModal`, `SelectQualityModalContent` | Set quality |
| `ReleaseGroup/` | `SelectReleaseGroupModal`, `SelectReleaseGroupModalContent` | Set release group |
| `IndexerFlags/` | `SelectIndexerFlagsModal`, `SelectIndexerFlagsModalContent` | Toggle flags |
| `ReleaseType/` | `SelectReleaseTypeModal`, `SelectReleaseTypeModalContent` | Choose release type |

## Flow

```
User opens "Manual Import" → folder picker modal
        ↓ User picks folder
GET /api/v5/manualimport?folder=… → list of files w/ parser guesses
        ↓ List<ManualImportResource> { path, series, seasonNumber, episodes, quality, languages, rejections, … }
Display table; each cell editable via per-cell modal
        ↓ User reviews, corrects mismatches
        ↓ User selects "Move" or "Copy" import mode
        ↓ User clicks "Import"
POST /api/v5/manualimport with selected rows → backend imports
        ↓ EpisodeFile rows created, files moved/copied, events fire
```

## Manga Adaptation Notes

This module needs updates per the Series→Manga, Episode→Chapter, Season→Volume rename:

| Mangarr | Manga |
|--------|-------|
| `Series/SelectSeriesModal` | `Manga/SelectMangaModal` (Plan 25-04 Task 2 — landed 2026-05-18) |
| `Episode/SelectEpisodeModal` | `Chapter/SelectChapterModal` (Plan 25-04 Task 1 — landed 2026-05-18) |
| `Season/SelectSeasonModal` | (deleted Plan 25-04 Task 3 — no manga peer per DOMAIN-02) |
| Quality/Language/ReleaseGroup | Reusable; adjust enums |
| `ReleaseType` | New types: `singleChapter`/`multiChapter`/`volumePack` |

The table-with-editable-cells pattern is reusable. The per-cell modals just need updated entity types.

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/Mangarr.Api.V5/ManualImport/](../../../src/Mangarr.Api.V5/ManualImport/) — Backend
- [../../../src/NzbDrone.Core/MediaFiles/CLAUDE.md](../../../src/NzbDrone.Core/MediaFiles/CLAUDE.md) — Import logic
- [../InteractiveSearch/CLAUDE.md](../InteractiveSearch/CLAUDE.md) — Sibling: manual release search
