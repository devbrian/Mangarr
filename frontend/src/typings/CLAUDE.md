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

- **Names mirror backend resources** — `SeriesResource` (C#) ↔ `Series` (TS, declared in `frontend/src/Series/Series.ts`, not here). The `typings/` folder is for **shared / cross-cutting** types.
- Domain types like `Series`, `Episode`, `EpisodeFile` live in their respective feature module (`Series/Series.ts`, etc.), not here.
- `Field.ts` is critical: it defines the type for dynamically-built provider forms (driven by backend `ClientSchema`).

## Related Type Locations

| Type | Defined In |
|------|-----------|
| `Series` | `frontend/src/Series/Series.ts` |
| `Episode` | `frontend/src/Episode/Episode.ts` |
| `Season` | (inline in Series.ts) |
| `EpisodeFile` | `frontend/src/EpisodeFile/EpisodeFile.ts` |
| `Quality` | `frontend/src/Quality/Quality.ts` |
| `Language` | `frontend/src/Language/Language.ts` |
| `Tag` | `frontend/src/Tags/...` |
| `Manga` | `frontend/src/Manga/Manga.ts` (Phase 7 Plan 07-03) |
| `Chapter` | `frontend/src/Chapter/Chapter.ts` (Phase 7 Plan 07-03) |
| `AddMangaResult` / `AddMangaPayload` | `frontend/src/AddManga/AddManga.ts` (Phase 7 Plan 07-03) |

## Manga Adaptation Notes

| Action | Effort |
|--------|--------|
| Rename `Series.ts` (in feature module) → `Manga.ts` | High (many imports) |
| Rename `Episode.ts` → `Chapter.ts` | High |
| Update fields in feature-module type files (remove TV-specific, add manga) | High |
| Keep most `typings/*.ts` files unchanged (system types are agnostic) | Low |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Series type
- [../Episode/CLAUDE.md](../Episode/CLAUDE.md) — Episode type
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga type (Phase 7 Plan 07-03)
- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Chapter type (Phase 7 Plan 07-03)
- [../AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — AddManga flow types (Phase 7 Plan 07-03)
- [../../../src/Sonarr.Api.V5/CLAUDE.md](../../../src/Sonarr.Api.V5/CLAUDE.md) — Backend resources these types mirror
