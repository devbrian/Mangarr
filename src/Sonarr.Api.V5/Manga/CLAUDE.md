# Sonarr.Api.V5/Manga (Phase 2 + Phase 6 Developer Endpoints)

## Purpose

v1 developer REST endpoints for the manga domain. Phase 2 shipped the core CRUD (`Manga` + `Lookup` + `Links`); Phase 6 added the pipeline surface (`History`, `Blocklist`, `Queue`, `Release`, `Wanted/Missing`). UI lives in Phase 7. All endpoints carry `[V5ApiController]` (admin X-Api-Key requirement per RESEARCH §Security Domain V4).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\Manga`

## Key Files

### Phase 2 — core CRUD

| File | Purpose |
|------|---------|
| `MangaResource.cs` + `MangaResourceMapper` | DTO with singular cross-source IDs (`Guid? MangaDexId`, `int? MalId`, `int? AniListId`) per CONTEXT specifics |
| `MangaController.cs` | GET/POST/PUT/DELETE `/api/v5/manga` (RestControllerWithSignalR) |
| `MangaLookupController.cs` | GET `/api/v5/manga/lookup?term=` — META-01 (primary metadata source via `IMetadataSourceFactory.GetPrimary`) |
| `MangaLinksController.cs` | POST `/api/v5/manga/{id}/links` — manual relink per D-23 (NO auto-validation against secondary sources) |

### Phase 6 Plan 06-09 — pipeline surface

| File | Purpose |
|------|---------|
| `Subresources/MangaSubresource.cs` | Minimal Manga summary (Id + Title) reused across all Phase 6 controller payloads |
| `Subresources/ChapterSubresource.cs` | Minimal Chapter summary (Id + MangaId + ChapterNumber + Title + TranslatedLanguage) |
| `History/ChapterHistoryResource.cs` + `ChapterHistoryResourceMapper` | DTO + mapper for paged history listing (HISTORY-01..03) |
| `History/ChapterHistoryController.cs` | GET `/api/v5/manga/history` paged + POST `/failed/{id}/retry` (HISTORY-03) |
| `Blocklist/MangaBlocklistResource.cs` + `MangaBlocklistResourceMapper` | DTO + mapper for blocklist listing (BLOCK-01..02) |
| `Blocklist/MangaBlocklistBulkResource.cs` | Bulk-delete request body (`Ids : List<int>`) |
| `Blocklist/MangaBlocklistController.cs` | GET `/api/v5/manga/blocklist` paged + DELETE id + DELETE bulk (BLOCK-02) |
| `Queue/MangaQueueResource.cs` + `MangaQueueResourceMapper` | DTO + mapper for in-flight queue projection (PIPELINE-03) |
| `Queue/MangaQueueController.cs` | GET `/api/v5/manga/queue` + DELETE id + SignalR fan-out via `IHandle<MangaQueueUpdatedEvent>` |
| `Release/MangaReleaseResource.cs` + `MangaReleaseResourceMapper` | DTO + mapper for ranked release listing (PIPELINE-01 Interactive Search) |
| `Release/MangaReleaseController.cs` | GET `/api/v5/manga/release?chapterId=` + POST grab (PIPELINE-01) |
| `Wanted/MissingChapterResource.cs` + `MissingChapterResourceMapper` | DTO + mapper for monitored chapters without files (WANTED-01..03) |
| `Wanted/MissingChaptersController.cs` | GET `/api/v5/manga/wanted/missing` paged with monitored / mangaIds / languages / ageRating filters (WANTED-01..03) |

## Endpoints

### Phase 2 — core CRUD

- `GET    /api/v5/manga` — list all manga (covers mapped to local URLs)
- `GET    /api/v5/manga/{id}` — get one manga
- `POST   /api/v5/manga` — add a manga (delegates to `IAddMangaService.AddManga`; requires at least one of MangaDexId/MalId/AniListId)
- `PUT    /api/v5/manga/{id}` — update mutable fields via `Manga.ApplyChanges` (canonical IDs immutable here per Plan 02-09)
- `DELETE /api/v5/manga/{id}` — delete (optionally with `?deleteFiles=true`)
- `GET    /api/v5/manga/lookup?term=` — META-01 search via primary metadata source
- `POST   /api/v5/manga/{id}/links` — manual relink per D-23 (BYPASSES `CrossSourceIdResolver`)

### Phase 6 — pipeline surface

- `GET    /api/v5/manga/history` — paged history; filters: `eventType`, `chapterId`, `downloadId`, `mangaIds[]`, `languages[]`, `includeSubresources[]=manga,chapter`
- `POST   /api/v5/manga/history/failed/{id}/retry` — manual retry escape hatch (HISTORY-03); pushes `ChapterSearchCommand`
- `GET    /api/v5/manga/blocklist` — paged blocklist; filters: `mangaIds[]`, `includeManga`
- `DELETE /api/v5/manga/blocklist/{id}` — delete one
- `DELETE /api/v5/manga/blocklist/bulk` — delete many (body: `{ ids: [...] }`)
- `GET    /api/v5/manga/queue` — full queue projection (no DB paging — static-list projection from `TrackedDownloadRefreshedEvent`)
- `DELETE /api/v5/manga/queue/{id}` — remove an in-flight item
- `GET    /api/v5/manga/release?chapterId=` — Interactive Search modal (PIPELINE-01); fans out to all enabled manga indexers, runs Decision Engine, returns ranked Approved/Rejected list
- `POST   /api/v5/manga/release` — grab a release (body = `MangaReleaseResource` returned from GET; cached `RemoteChapter` round-tripped by `(IndexerId, Guid)`)
- `GET    /api/v5/manga/wanted/missing` — paged missing chapters; filters: `monitored=true&mangaIds[]&languages[]&ageRating&includeManga`

## Patterns / Conventions

- **All endpoints `[V5ApiController]`** → admin X-Api-Key requirement (RESEARCH §Security Domain V4 — threat T-AUTHN-01).
- **`RestControllerWithSignalR<TResource, TModel>` + `IHandle<TEvent>`** for live-pushed entities (`MangaController` for the manga aggregate; `MangaQueueController` for the in-flight queue projection). The base type requires `where TModel : ModelBase, new()` — `MangaQueueItem` inherits `ModelBase` even though it is a pure projection POCO (Plan 06-09 Rule 2 deviation; mirrors TV `Queue` precedent).
- **`PagingRequestResource` query → `MapToPagingSpec` filter-expression chain**: every paged GET endpoint composes `pagingSpec.FilterExpressions.Add(c => ...)` calls based on optional query params; the BasicRepository pipeline runs them through the SqlBuilder.
- **`includeSubresources[]` query** drives optional Manga / Chapter hydration via service-layer `Get` calls at the controller layer (Plan 06-03 / 06-09 D-21 decision: repositories do NOT JOIN — controllers hydrate when asked).
- **Resource POCOs flatten entity → wire shape**: `MangaQueueResource` does not leak `RemoteChapter` (in-process EF reference); subresource POCOs (`MangaSubresource`, `ChapterSubresource`) carry only id + display fields.
- **`ICached<RemoteChapter>` round-trip between GET search and POST grab** (`MangaReleaseController`): keyed on `(IndexerId, Guid)` with 30-min TTL; mirrors TV `ReleaseController._remoteEpisodeCache`.
- **Wire-level RemoteEpisode shim for the manga grab path**: `MangaReleaseController.DownloadRelease` constructs `RemoteEpisode { Series = { Id = mangaId }, Episodes = [{ Id = chapterId }], Release = ... }` and calls `IDownloadService.DownloadReport` — Phase 4 D-10's `Protocol == DownloadProtocol.Http` early-return guard routes the manga release into `InProcessImageDownloadClient`. Phase 8 collapse drops the shim when the unified `IDownloadService` lands.
- **D-04 IsSynthetic-treated-identically pattern** at the REST layer: `MissingChaptersController` does NOT add a `WHERE IsSynthetic = false` filter — synthetic rows (Phase 2 D-17 metadata-only-count fallback) are surfaced alongside real rows. A future `excludeSynthetic` query param could opt-in to the filter; v1 default is INCLUDE.
- **HISTORY-03 retry shape**: POST `/failed/{id}/retry` pushes a single-element `ChapterSearchCommand` (Plan 06-06) for the failed chapter; the decision engine skips the now-blocklisted release (Plan 06-04 D-19 + Pitfall 5 normalization) and grabs next-best. This is the user's manual escape hatch after the D-13 `MaxAutoRetriesPerChapter` budget exhausts.
- **Singular cross-source IDs** (`Guid?`, `int?`) on `MangaResource` reflect the manga 1:1-across-sources model.

## Manga Adaptation Notes

- Phase 7 wires React UI against these endpoints:
  - **Add Manga page** → `/lookup` + POST `/manga` + MonitorTypes dropdown + Search-on-Add toggle + per-Manga `UpgradeAllowedOverride` field
  - **Activity panel** → GET/DELETE `/queue` (with SignalR fan-out) + GET `/history` + POST `/history/failed/{id}/retry` + GET/DELETE `/blocklist`
  - **Wanted view** → GET `/wanted/missing` with React filter chips for `mangaIds`, `languages`, `ageRating`
  - **Interactive Search modal (PIPELINE-01)** → reuse existing React `InteractiveSearch/` components against GET `/release?chapterId=` + POST `/release`
  - **Settings → Notifications → Komga + Kavita** add-flow (Plans 06-10 / 06-11) with auto-test on save
- **Phase 8 rename**: when the `Series → Manga` cutover lands, this directory becomes the canonical "primary domain" controller — the existing `Series/`, `History/`, `Blocklist/`, `Queue/`, `Release/`, `Wanted/` peers are deleted; the `Protocol == DownloadProtocol.Http` early-return guard in `CompletedDownloadService` disappears with `ImportApprovedEpisodes`; the `MangaReleaseController.BuildRemoteEpisodeShim` thin shim disappears with the `IDownloadService` unification.

## Cross-References

- Sonarr V5 analogs (each Phase 6 controller mirrors a TV peer):
  - `src/Sonarr.Api.V5/History/HistoryController.cs` (history listing + retry)
  - `src/Sonarr.Api.V5/Blocklist/BlocklistController.cs` (blocklist CRUD)
  - `src/Sonarr.Api.V5/Queue/QueueController.cs` (in-flight queue + SignalR)
  - `src/Sonarr.Api.V5/Release/ReleaseController.cs` (Interactive Search + Grab)
  - `src/Sonarr.Api.V5/Wanted/MissingController.cs` (Missing list)
- Phase 6 backing services:
  - `src/NzbDrone.Core/History/Manga/IChapterHistoryService.cs` (Plan 06-03)
  - `src/NzbDrone.Core/Blocklisting/Manga/IMangaBlocklistService.cs` (Plan 06-04)
  - `src/NzbDrone.Core/Queue/Manga/IMangaQueueService.cs` (Plan 06-05)
  - `src/NzbDrone.Core/IndexerSearch/Manga/IMangaSearchForReleases.cs` (Plan 06-06)
  - `src/NzbDrone.Core/Manga/IChapterService.cs` (paged `ChaptersWithoutFiles` overload added by Plan 06-09)
- Manga model + services: [`src/NzbDrone.Core/Manga/CLAUDE.md`](../../NzbDrone.Core/Manga/CLAUDE.md)
- AddMangaService (META-02 orchestrator, D-19/D-20 wiring): [`src/NzbDrone.Core/Manga/AddMangaService.cs`](../../NzbDrone.Core/Manga/AddMangaService.cs)
- MetadataSource scaffold (D-14, D-15): [`src/NzbDrone.Core/MetadataSource/CLAUDE.md`](../../NzbDrone.Core/MetadataSource/CLAUDE.md)
- Manga cover mapper (Plan 02-09 sibling per RESEARCH §Pattern 5): [`src/NzbDrone.Core/MediaCover/MangaMediaCoverService.cs`](../../NzbDrone.Core/MediaCover/MangaMediaCoverService.cs)
