# Sonarr.Api.V5

## Purpose

REST API controllers for **API version 5** — the **current primary API** consumed by the React frontend and external integrations (e.g., Plex/Kodi/scripts).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\`

**File count**: ~149 .cs files (44 controllers across 30 modules).

## URL Prefix

All endpoints are prefixed with **`/api/v5`** (configured by the `[V5ApiController]` attribute and route conventions in `NzbDrone.Host`).

## Controller Inventory (44 controllers)

### Core Resources (Series / Episodes / Files / Calendar / Wanted)

| Module | Controllers | Notes |
|--------|------------|-------|
| `Series/` | `SeriesController`, `SeriesLookupController`, `SeriesImportController`, `SeriesEditorController`, `SeriesFolderController` | CRUD, search/lookup, bulk edit, import-from-folder. **→ Manga** |
| `Episodes/` | `EpisodeController`, `RenameEpisodeController` | Episode listing per series, rename preview. **→ Chapter** |
| `EpisodeFiles/` | `EpisodeFileController` | File CRUD + bulk delete. **→ ChapterFile** |
| `Calendar/` | `CalendarController`, `CalendarFeedController` | Upcoming + iCal feed. **→ Manga release schedule** |
| `Wanted/` | `MissingController`, `CutoffController` | Missing episodes, episodes below cutoff. **→ Missing/Cutoff Chapters** |

### Configuration

| Module | Controllers |
|--------|-------------|
| `Profiles/` | (resources only — provider config controllers under Settings) |
| `Qualities/` | `QualityDefinitionController` |
| `CustomFormats/` | (resource files only) |
| `Tags/` | `TagController`, `TagDetailsController` |
| `RootFolders/` | `RootFolderController` |
| `RemotePathMappings/` | `RemotePathMappingController` |

### Settings Subsection
| File | Purpose |
|------|---------|
| `SettingsController.cs` | Cross-cutting settings |
| `GeneralSettingsController.cs` | General settings |
| `IndexerSettingsController.cs` | Indexer global options |
| `MediaManagementSettingsController.cs` | File-management settings |
| `NamingSettingsController.cs` | File naming patterns |
| `UiSettingsController.cs` | UI preferences |
| `UpdateSettingsController.cs` | Update channel / branch |

### Download Pipeline

| Module | Controllers | Notes |
|--------|-------------|-------|
| `Indexers/` | `IndexerController` | Indexer plugin CRUD |
| `Connections/` | `ConnectionController` | Download client CRUD (V5 unified name) |
| `Blocklist/` | `BlocklistController` | Blocked releases |
| `Queue/` | `QueueController`, `QueueActionController`, `QueueDetailsController`, `QueueStatusController` | Active downloads |
| `History/` | `HistoryController` | Grab/import history |
| `Release/` | `ReleaseController`, `ReleasePushController` | Available releases + manual push |
| `ManualImport/` | `ManualImportController` | Manual import workflow |

### System & Operations

| Module | Controllers |
|--------|-------------|
| `Commands/` | `CommandController` |
| `System/` | `SystemController` |
| `Health/` | `HealthController` |
| `Logs/` | `LogController`, `LogFileController`, `UpdateLogFileController` |
| `Update/` | `UpdateController` |
| `DiskSpace/` | `DiskSpaceController` |
| `FileSystem/` | `FileSystemController` |
| `Parse/` | `ParseController` (utility for testing parser) |
| `Localization/` | `LocalizationController`, `LanguageController` |
| `CustomFilters/` | `CustomFilterController` |
| `ImportLists/` | `ImportListExclusionController` |
| `Metadata/` | `MetadataController` |
| `Provider/` | (provider-base resource — used by indexer/dlclient/etc. lookup) |
| `SeasonPass/` | `SeasonPassController` (bulk season operations — possibly N/A for manga) |

## Endpoints — Common Examples

### SeriesController
```
GET    /api/v5/series                 List all
GET    /api/v5/series/{id}            Get one
POST   /api/v5/series                 Add new
PUT    /api/v5/series/{id}            Update
DELETE /api/v5/series/{id}            Delete (?deleteFiles=, ?addImportListExclusion=)
GET    /api/v5/series/lookup?term=X   Metadata search
PUT    /api/v5/series/editor          Bulk edit
DELETE /api/v5/series/editor          Bulk delete
```

### EpisodeController
```
GET /api/v5/episode?seriesId=123      List episodes for a series
GET /api/v5/episode/{id}              Single episode
PUT /api/v5/episode/{id}              Update (mainly toggle monitored)
PUT /api/v5/episode/monitor           Bulk toggle monitored: { episodeIds:[…], monitored:true }
```

### CommandController
```
GET    /api/v5/command                List commands (running + recent)
GET    /api/v5/command/{id}           Single command status
POST   /api/v5/command                Execute new command — body { name:"RefreshSeries", … }
DELETE /api/v5/command/{id}           Cancel
```

## Resource Pattern

Each controller has a corresponding `*Resource` (DTO) class:

```csharp
public class SeriesResource : RestResource     // RestResource provides Id
{
    public string Title { get; set; }
    public string Path { get; set; }
    public List<SeasonResource> Seasons { get; set; }
    public SeriesStatisticsResource Statistics { get; set; }
    // …
}
```

Plus a static mapper:

```csharp
// SeriesResource.cs (or SeriesResourceMapper.cs)
public static class SeriesResourceMapper
{
    public static SeriesResource ToResource(this Series model) => new()
    {
        Id    = model.Id,
        Title = model.Title,
        Path  = model.Path,
        // …
    };

    public static Series ToModel(this SeriesResource resource) => new()
    {
        Id    = resource.Id,
        Title = resource.Title,
        // …
    };
}
```

Resource validation lives in `*ResourceValidator.cs` files and uses `ResourceValidator<T>` (FluentValidation).

## Common Commands (POST /api/v5/command)

| Command | Purpose |
|---------|---------|
| `RefreshSeries` | Re-fetch metadata from MetadataSource |
| `RescanSeries` | Re-scan disk for files |
| `SeriesSearch` | Search all monitored eps in a series |
| `EpisodeSearch` | Search a single episode |
| `SeasonSearch` | Search a whole season |
| `MissingEpisodeSearch` | Search all monitored missing eps |
| `CutoffUnmetEpisodeSearch` | Search all eps below cutoff |
| `RssSync` | Run RSS sync now |
| `RenameFiles` | Rename files for selected eps |
| `Backup` | Trigger manual backup |
| `ApplicationUpdate` | Trigger self-update |
| `MessagingCleanup`, `Housekeeping`, `CheckHealth` | Maintenance |

## Authentication

All endpoints require auth (unless explicitly opted out) via:
- `X-Api-Key` header
- `?apikey=…` query
- Cookie session (for the UI)

API key is in General Settings; resettable.

## Manga Adaptation Plan

### Conceptual Renames (when migration moves to Mangarr)
| Sonarr Controller | Mangarr Controller |
|-------------------|--------------------|
| `SeriesController` | `MangaController` |
| `SeriesLookupController` | `MangaLookupController` |
| `SeriesImportController` | `MangaImportController` |
| `SeriesEditorController` | `MangaEditorController` |
| `EpisodeController` | `ChapterController` |
| `EpisodeFileController` | `ChapterFileController` |
| `RenameEpisodeController` | `RenameChapterController` |
| `MissingController` | `MangaMissingController` |
| `CutoffController` | `MangaCutoffController` |
| `SeasonPassController` | `VolumePassController` (or remove if N/A) |

### New Endpoints to Add
- `/api/v5/manga` — Manga CRUD
- `/api/v5/manga/lookup` — MangaDex/AniList search
- `/api/v5/chapter` — Chapter management
- `/api/v5/volume` — Volume (if implemented)

### Strategy Note

For backward compatibility with any external integrations that consume `/api/v5/series`, consider keeping aliased routes during the transition (e.g. add `[Route("api/v5/manga")]` AND keep `[Route("api/v5/series")]` on the same controller temporarily).

## Phase 6 Manga V5 Sibling Controllers

Phase 6 Plan 06-09 ships 5 manga-side V5 controllers under `/api/v5/manga/` that close the v1 PIPELINE/HISTORY/BLOCK/WANTED requirements. Each is a parallel sibling to a TV V5 controller; **Phase 8 cleanup** will collapse the sibling pairs when `Tv/` deletes (and `Sonarr.Api.V3/` is dropped).

| Sibling | TV Analog | Phase 6 Requirements |
|---------|-----------|----------------------|
| `Manga/History/ChapterHistoryController.cs` at `/api/v5/manga/history` | `History/HistoryController.cs` | HISTORY-01..03 — paged GET with `eventType / chapterId / downloadId / mangaIds[] / languages[] / includeSubresources[]` filters; POST `/failed/{id}/retry` pushes `ChapterSearchCommand` (HISTORY-03 manual retry escape hatch after auto-retry budget exhausts) |
| `Manga/Blocklist/MangaBlocklistController.cs` at `/api/v5/manga/blocklist` | `Blocklist/BlocklistController.cs` | BLOCK-01..02 — paged GET with `mangaIds[] / includeManga` filters; `[RestDeleteById]` single-row delete; `[HttpDelete("bulk")]` bulk delete via `MangaBlocklistBulkResource { Ids: List<int> }`; carries D-11 release-identity triple `(SourceKey, ReleaseGuid, SourceTitle)` |
| `Manga/Queue/MangaQueueController.cs` at `/api/v5/manga/queue` | `Queue/QueueController.cs` | PIPELINE-03 — `RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>` + `IHandle<MangaQueueUpdatedEvent>`; GET returns full projection (no DB paging — static-list from `TrackedDownloadRefreshedEvent`); SignalR resource name = `mangaqueue` |
| `Manga/Release/MangaReleaseController.cs` at `/api/v5/manga/release` | `Release/ReleaseController.cs` | PIPELINE-01 — Interactive Search modal; GET `?chapterId=` runs `IMangaSearchForReleases.ChapterSearch`; POST grabs the cached RemoteChapter via a thin `RemoteEpisode` shim (`Series = { Id = manga.Id }`, `Episodes = [{ Id = chapter.Id }]`); Phase 4 D-10 `Protocol == DownloadProtocol.Http` early-return routes into `InProcessImageDownloadClient`. ICached<RemoteChapter> 30-min TTL mirrors TV's `_remoteEpisodeCache` |
| `Manga/Wanted/MangaMissingController.cs` at `/api/v5/manga/wanted/missing` | `Wanted/MissingController.cs` | WANTED-01..03 — paged GET with `monitored=true & mangaIds[] & languages[] & ageRating & includeSubresources[]=Manga` filters; backed by new `IChapterService.ChaptersWithoutFiles(PagingSpec)` paged overload; D-04 GUARD: NO `IsSynthetic` filter (synthetic rows surface alongside real rows by default). Renamed 2026-05-06 from `MissingChaptersController` for naming consistency with the rest of the manga V5 namespace (`Manga` prefix as anti-collision strategy, matching `MangaCutoffController`/`MangaQueueController`/etc.). After canonical-resource-reuse follow-up (2026-05-06): returns canonical `ChapterResource` (NO custom paged-row resource class — mirrors TV `MissingController` reusing `EpisodeResource`; replaces deleted `MissingChapterResource` POCO + `bool includeManga` flag with TV-mirroring `MangaMissingSubresource` enum-array). |
| `Manga/Wanted/MangaCutoffController.cs` at `/api/v5/manga/wanted/cutoff` | `Wanted/CutoffController.cs` | Plan 12-12 F-CUTOFF closure — paged GET with `monitored=true & mangaIds[] & includeSubresources[]=Manga` filters; backed by `IChapterCutoffService.ChaptersWhereCutoffUnmet`. Drops TV's languages/ageRating filters (cutoff is profile-driven). After F-CUTOFF-SIGNALR follow-up + canonical-resource-reuse follow-up (both 2026-05-06): extends `RestControllerWithSignalR<,>` + 3 IHandle subscriptions; returns canonical `ChapterResource` (NO custom paged-row resource class — mirrors TV `CutoffController` reusing `EpisodeResource`; replaces deleted `MangaCutoffResource` POCO + `bool includeManga` flag with TV-mirroring `MangaCutoffSubresource` enum-array). |
| `Manga/Subresources/MangaSubresource.cs + ChapterSubresource.cs` | (none — new pattern) | Shared minimal-shape POCOs reused across all Phase 6 controller payloads (avoids leaking full `MangaResource` shape on every nested hydration). Phase 8 cleanup: collapse with the `Series` subresource pattern. |

**Required upstream substrate added by Plan 06-09 (Rule 2 + Rule 3 deviations):**
- `MangaQueueItem` now inherits `ModelBase` (Rule 2 — needed for `RestControllerWithSignalR<TResource, TModel>` generic constraint; mirrors TV `Queue` precedent)
- `IChapterService.ChaptersWithoutFiles(PagingSpec)` paged overload added (Rule 3 — Plan 06-01 only shipped the non-paged `AllMissingMonitoredChapters()` variant; the paged variant pre-pends `c => c.ChapterFileId == null` onto `FilterExpressions`)

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Architecture
- [Sonarr.Api.V3/CLAUDE.md](../Sonarr.Api.V3/CLAUDE.md) — Legacy V3 API
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) — Base infrastructure
- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) — Domain logic
- [frontend/CLAUDE.md](../../frontend/CLAUDE.md) — Frontend consuming this API
