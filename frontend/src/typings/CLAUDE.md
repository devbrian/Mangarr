# typings/

## Purpose

TypeScript interface definitions mirroring the backend REST API resources (DTOs). These are the **frontend's view** of the backend — every API response is typed via one of these.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\typings\`

## Top-Level Types (40+ files)

### System / Activity
| File | Backend `*Resource` Equivalent |
|------|-------------------------------|
| `Backup.ts` | `BackupResource` |
| `DiskSpace.ts` | `DiskSpaceResource` |
| `Health.ts` | `HealthResource` |
| `LogEvent.ts` / `LogFile.ts` | log resources |
| `Queue.ts` | `QueueResource` (TV) |
| `MangaQueueItem.ts` | `MangaQueueResource` (Phase 6 Plan 06-09) |
| `History.ts` | `HistoryResource` (TV) |
| `ChapterHistory.ts` | `ChapterHistoryResource` (Phase 6 Plan 06-09) |
| `Blocklist.ts` | `BlocklistResource` (TV) |
| `MangaBlocklist.ts` | `MangaBlocklistResource` (Phase 6 Plan 06-09) |
| `Calendar.ts` | `CalendarResource` |
| `MediaInfo.ts` | `MediaInfoResource` |

### Configuration / Providers
| File | Purpose |
|------|---------|
| `CustomFormat.ts` / `CustomFormatSpecification.ts` | Custom format types |
| `DownloadClient.ts` | Download client config |
| `ImportList.ts` | Import list config |
| `Indexer.ts` | Indexer config |
| `Provider.ts` | Generic provider base type |
| `RootFolder.ts` | Root folder type |

### Forms / UI
| File | Purpose |
|------|---------|
| `callbacks.ts` | Common callback signatures |
| `inputs.ts` | Form input types |
| `props.ts` | Prop helpers |
| `Field.ts` | Provider form-field schema |
| `Table.ts` | Table column config types |

### Settings
| Folder/File | Purpose |
|-------------|---------|
| `Settings/` | Per-settings-page type definitions |

## Conventions

- **Names mirror backend resources** — `MangaResource` (C#) ↔ `Manga` (TS, declared in `frontend/src/Manga/Manga.ts`, not here). The `typings/` folder is for **shared / cross-cutting** types.
- Domain types like `Manga`, `Chapter`, `ChapterFile` live in their respective feature module (`Manga/Manga.ts`, `Chapter/Chapter.ts`, etc.), not here.
- `Field.ts` is critical: it defines the type for dynamically-built provider forms (driven by backend `ClientSchema`).

## Related Type Locations

| Type | Defined In |
|------|-----------|
| `Quality` | `frontend/src/Quality/Quality.ts` (Sonarr-shape; Phase 5 D-04 dropped from manga decision flow but type retained for back-compat with TV-shape consumers) |
| `Language` | `frontend/src/Language/Language.ts` |
| `Tag` | `frontend/src/Tags/...` |
| `Manga` | `frontend/src/Manga/Manga.ts` (Phase 7 Plan 07-03; Phase 17.3 Plan 17.3-12 D-13 trimmed TV-shape carry-over fields) |
| `Chapter` | `frontend/src/Chapter/Chapter.ts` (Phase 7 Plan 07-03) |
| `AddMangaResult` / `AddMangaPayload` | `frontend/src/AddManga/AddManga.ts` (Phase 7 Plan 07-03) |

Note: Sonarr `Series` / `Episode` / `EpisodeFile` / `Season` TypeScript type
files lived under their respective `frontend/src/{Series,Episode,EpisodeFile,Season}/`
feature modules until Phase 17.3 Plan 17.3-13 (D-09/D-10) atomic stub-dir
delete retired the entire subtree. The manga peers above are now canonical.

## Manga Adaptation Notes

| Action | Status |
|--------|--------|
| Rename `Series.ts` (in feature module) → `Manga.ts` | **Done** — Phase 7 Plan 07-03 (Manga.ts shipped); Phase 17.3 Plan 17.3-12 D-13 (carry-over trim); Plan 17.3-13 (Series.ts stub deleted) |
| Rename `Episode.ts` → `Chapter.ts` | **Done** — Phase 7 Plan 07-03 (Chapter.ts shipped); Plan 17.3-13 (Episode.ts stub deleted) |
| Update fields in feature-module type files | **Done** — Plan 17.3-12 D-13 stripped Sonarr-shape carry-over fields from Manga.ts |
| Keep most `typings/*.ts` files unchanged | **Done** — `typings/MangaQueueItem.ts` / `typings/ChapterHistory.ts` / `typings/MangaBlocklist.ts` shipped Phase 6 Plan 06-09; rest of `typings/` is media-agnostic |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga type (Phase 7 Plan 07-03)
- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Chapter type (Phase 7 Plan 07-03)
- [../AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — AddManga flow types (Phase 7 Plan 07-03)
- [../../../src/Mangarr.Api.V5/CLAUDE.md](../../../src/Mangarr.Api.V5/CLAUDE.md) — Backend resources these types mirror
