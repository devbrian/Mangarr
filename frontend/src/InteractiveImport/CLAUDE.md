# InteractiveImport/

## Purpose

**Manual import** — user picks a folder of files, sees a per-file table with parsed series/season/episode/quality/language guesses, manually corrects mappings, and imports. Used when:
- Auto-import failed
- Files exist outside the watched download folder
- User wants to bulk-import an existing library


**File count**: ~48 files.

## Top-Level Files

| File | Purpose |
|------|---------|
| `InteractiveImportModal.tsx` | Modal entry point |
| `InteractiveImport.ts` | Type definitions |
| `ImportMode.ts` | enum: `auto` / `move` / `copy` / `chooseImportMode` |
| `ReleaseType.ts` | enum: `unknown` / `singleEpisode` / `multiEpisode` / `seasonPack` (still TV-shape values in source) |
| `interactiveImportFoldersStore.ts` | Zustand: recently used folders |
| `interactiveImportOptionsStore.ts` | Zustand: import options |
| `useInteractiveImport.ts` | Mutation hook |

## Subdirectory: Interactive/
| File | Purpose |
|------|---------|
| `InteractiveImportModalContent.tsx` | Main table with all rows |
| `InteractiveImportContent.tsx` | Inner content wrapper |
| `InteractiveImportRow.tsx` | One file row |
| `InteractiveImportRowCellPlaceholder.tsx` | Placeholder/loading cells |

## Per-Cell Selection Modals (one subdir per editable cell)

| Subdir | Modal | Purpose |
|--------|-------|---------|
| `Folder/` | `InteractiveImportSelectFolderModalContent`, `FavoriteFolderRow`, `RecentFolderRow` | Pick the source folder |
| `Chapter/` | `SelectChapterModal`, `SelectChapterModalContent` | Pick chapter(s) — Plan 25-04 Task 1 renamed `Episode/` → `Chapter/` per Pitfall 13 atomic-per-decision commit. **Shipped Phase 30 Plan 30-03 (2026-05-23+)** — Sonarr v5-develop `SelectEpisodeModal` port with R-5 aggressive strip (1 CBZ = 1 chapter per Phase 4 ARCHIVE invariant; no Season; no scene-numbering; no multi-episode-per-file slice logic). Testid family: `select-chapter-modal-*`. |
| `Manga/` | `SelectMangaModal`, `SelectMangaModalContent` | Pick or correct manga — Plan 25-04 Task 2 renamed `Series/` → `Manga/` per Pitfall 13. **Shipped Phase 30 Plan 30-03 (2026-05-23+)** — Sonarr v5-develop `SelectSeriesModal` port with title autocomplete picker over `useManga()` library entries (columns title/year/mangaDexId; `imdbId` dropped; `malId` deferred to v1.3+). Testid family: `select-manga-modal-*`. |
| `Season/` | (deleted Plan 25-04 Task 3 — manga has no season per DOMAIN-02; SelectSeasonModal stub + per-row season cell + season-modal trigger removed) | n/a |
| `Language/` | `SelectLanguageModal`, `SelectLanguageModalContent` | Set language(s) |
| `Quality/` | `SelectQualityModal` | Set quality |
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
        ↓ ChapterFile rows created, files moved/copied, events fire
```

(The flow's `series` / `seasonNumber` / `episodes` field names are the inherited
Sonarr `ManualImportResource` wire shape — the per-cell modals above are the
landed manga peers.)

## Automation Test Surface (Phase 30+)

The Plan 30-03 modal-body ship is accompanied by a shared cross-page helper at `src/NzbDrone.Automation.Test/Flows/InteractiveImportFlow.cs` (Phase 18 D-08 first-class shared-flow convention). It exposes 5 static methods on `IPage`:

| Method | Drives |
|--------|--------|
| `SelectMangaAsync(page, mangaId)` | The autocomplete picker shipped in Plan 30-03 Task 1 |
| `SelectChaptersAsync(page, chapterIds)` | The multi-select picker shipped in Plan 30-03 Task 2 |
| `OpenFromQueueAsync(page, rootUri)` | Activity/Queue toolbar entry point (`Queue.tsx:409`) |
| `OpenFromQueueRowAsync(page, rootUri, queueItemId)` | Per-row Queue entry point (`QueueRow.tsx:430`) |
| `OpenFromMissingAsync(page, rootUri)` | Wanted/Missing toolbar entry point (`Missing.tsx:375`; Plan 12-11 LOCK guard removed in Phase 25-05) |

Each of the 3 entry-point methods is exercised by at least one fixture in `src/NzbDrone.Automation.Test/Tests/InteractiveImport/InteractiveImportEntryPointFixture.cs` per CONTEXT.md D-01 verify pass. References:
- `.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-PATTERNS.md` §Plan 30-03
- `.planning/phases/30-manual-import-settings-completeness-v1-2-inserted-2026-05-23/30-RESEARCH.md` §3 (SelectSeriesModal / SelectEpisodeModal templates)

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/Mangarr.Api.V5/ManualImport/](../../../src/Mangarr.Api.V5/ManualImport/) — Backend
- [../../../src/NzbDrone.Core/MediaFiles/CLAUDE.md](../../../src/NzbDrone.Core/MediaFiles/CLAUDE.md) — Import logic
- [../InteractiveSearch/CLAUDE.md](../InteractiveSearch/CLAUDE.md) — Sibling: manual release search
