# Mangarr.Api.V5

## Purpose

REST API controllers for **API version 5** — the **sole REST surface** consumed by the React frontend and external integrations. (The Sonarr-era V3 API was wholesale-deleted in Phase 15 Plan 15-06.)

**Absolute Path**: `src/Mangarr.Api.V5/`

## URL Prefix

All endpoints are prefixed with **`/api/v5`** (configured by the `[V5ApiController]` attribute + route conventions in `NzbDrone.Host`). Routes derive from the attribute string OR, when bare, from the controller's `*Resource` class name (ASP.NET Core case-insensitive routing).

## Controller Inventory (61 controllers)

Counts verified by `find src/Mangarr.Api.V5 -name '*Controller.cs'`. Domain entities are **Manga / Chapter / ChapterFile** — there are no Series/Episode/Season/Calendar/Quality controllers (those Sonarr peers were deleted in the Phase 15 hard-fork; see DIVERGENCE.md).

### Manga domain (`Manga/`)
| Module | Controllers | Sub-doc |
|--------|-------------|---------|
| `Manga/` | `MangaController`, `MangaLookupController`, `MangaEditorController`, `MangaFolderController`, `MangaLinksController`, `StrayChaptersController` | [Manga/CLAUDE.md](./Manga/CLAUDE.md) |
| `Manga/Chapter/` | `ChapterController`, `ChapterFileController`, `RenameChapterController` | [Manga/Chapter/CLAUDE.md](./Manga/Chapter/CLAUDE.md) |
| `Manga/Queue/` | `MangaQueueController`, `MangaQueueActionController`, `MangaQueueDetailsController`, `MangaQueueStatusController` | [Manga/Queue/CLAUDE.md](./Manga/Queue/CLAUDE.md) |
| `Manga/History/` | `ChapterHistoryController` | — |
| `Manga/Blocklist/` | `MangaBlocklistController` | — |
| `Manga/Release/` | `MangaReleaseController` | — |
| `Manga/Wanted/` | `MangaMissingController`, `MangaCutoffController` | — |

### Configuration & profiles
| Module | Controllers |
|--------|-------------|
| `Config/` | `DownloadClientConfigController`, `ImportListConfigController`, `MangaNamingConfigController` |
| `Profiles/` | `TranslationProfileController`, `CustomFormatProfileController`, `DelayProfileController`, `ReleaseProfileController` |
| `CustomFormats/` | `CustomFormatController` |
| `CustomFilters/` | `CustomFilterController` |
| `AutoTagging/` | `AutoTaggingController` (Phase 24 Plan 24-04; route `/api/v5/autotagging`, schema inlined — no separate spec controller) |
| `Tags/` | `TagController`, `TagDetailsController` |
| `RootFolders/` | `RootFolderController` |
| `RemotePathMappings/` | `RemotePathMappingController` |

### Settings (`Settings/`)
`SettingsController`, `GeneralSettingsController`, `IndexerSettingsController`, `MediaManagementSettingsController`, `UiSettingsController`, `UpdateSettingsController`.

### Download / indexer / metadata pipeline
| Module | Controllers | Notes |
|--------|-------------|-------|
| `Indexers/` | `IndexerController`, `IndexerFlagController` | Indexer plugin CRUD (sole `IIndexer` = `GatewayIndexer`) |
| `DownloadClient/` | `DownloadClientController` | route `/api/v5/downloadclient` (sole client = `GatewayDownloadClient`) |
| `Connections/` | `ConnectionController` | Notification CRUD — Sonarr v5 renamed `Notification`→`Connection`; route `/api/v5/connection`. FE `useConnections.ts` uses `PATH='/connection'`. |
| `ImportLists/` | `ImportListController`, `ImportListExclusionController` | [ImportLists/CLAUDE.md](./ImportLists/CLAUDE.md) |
| `Metadata/` | `MetadataController` | ComicInfo metadata writer (Phase 30 Plan 30-04); route `/api/v5/metadata` |
| `MetadataSource/` | `MetadataSourceController` | MangaDex/AniList/MAL/MangaBaka source CRUD + SetPrimary — [MetadataSource/CLAUDE.md](./MetadataSource/CLAUDE.md) |
| `Discovery/` | `DiscoveryController` | Filtered bulk-add browse (Phase 42) — [Discovery/CLAUDE.md](./Discovery/CLAUDE.md) |
| `ManualImport/` | `ManualImportController` | Manual import workflow |

### System & operations
| Module | Controllers |
|--------|-------------|
| `Commands/` | `CommandController` |
| `System/` | `SystemController`, `System/Backup/BackupController`, `System/Tasks/TaskController` |
| `Health/` | `HealthController` |
| `Logs/` | `LogController`, `LogFileController`, `UpdateLogFileController` |
| `Update/` | `UpdateController` |
| `DiskSpace/` | `DiskSpaceController` |
| `FileSystem/` | `FileSystemController` |
| `Localization/` | `LocalizationController`, `LanguageController` |

## Resource / mapper / validator pattern

Each controller has a `*Resource` (DTO) extending `RestResource` (provides `int Id`), a static `*ResourceMapper` with `ToResource()` / `ToModel()` extension methods, and (where validated) a `*Validator` using FluentValidation. Provider controllers (Indexer/DownloadClient/Connection/ImportList/MetadataSource/Metadata) extend `ProviderControllerBase` for the standard 10-endpoint ThingiProvider CRUD + schema + test + bulk surface.

## Common commands (POST /api/v5/command)

| Command | Purpose |
|---------|---------|
| `RefreshManga` | Re-fetch metadata from the primary MetadataSource |
| `RescanManga` | Re-scan disk for chapter files |
| `MangaSearch` / `ChapterSearch` | Search a manga / a single chapter |
| `MissingChapterSearch` / `CutoffUnmetChapterSearch` | Search monitored missing / below-cutoff chapters |
| `ImportListSync` | Run import-list sync now |
| `RenameFiles` | Rename chapter files |
| `Backup`, `ApplicationUpdate`, `Housekeeping`, `CheckHealth` | Maintenance |

## Authentication

All endpoints require auth (unless explicitly opted out) via `X-Api-Key` header, `?apikey=…` query, or cookie session (UI). API key lives in General Settings; resettable.

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Architecture
- [Mangarr.Http/CLAUDE.md](../Mangarr.Http/CLAUDE.md) — REST base / auth / middleware
- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) — Domain logic
- [frontend/CLAUDE.md](../../frontend/CLAUDE.md) — Frontend consuming this API
