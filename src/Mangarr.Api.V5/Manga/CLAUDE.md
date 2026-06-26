# Mangarr.Api.V5/Manga (Phase 2 + Phase 6 Developer Endpoints)

## Purpose

v1 developer REST endpoints for the manga domain. Phase 2 shipped the core CRUD (`Manga` + `Lookup` + `Links`); Phase 6 added the pipeline surface (`History`, `Blocklist`, `Queue`, `Release`, `Wanted/Missing`). UI lives in Phase 7. All endpoints carry `[V5ApiController]` (admin X-Api-Key requirement per RESEARCH §Security Domain V4).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Mangarr.Api.V5\Manga`

## Statistics (issue #335)

`MangaResource.Statistics` is populated from the SQL-aggregated `IMangaStatisticsService`
(`NzbDrone.Core/MangaStats/`) — the Sonarr `SeriesStatisticsService` analog — **not** an inline
per-manga chapter read. The former inline `MangaController.ComputeStatistics` (the "F-05 stopgap")
was retired when issue #335 fixed the previously-dead service. Wiring mirrors Sonarr's
`SeriesController`:
- `GetAll` runs ONE `MangaStatistics()` aggregate query, keys it by `MangaId`, and links per
  resource via `LinkMangaStatistics` (a dictionary miss → zeroed `MangaStatisticsResource`,
  preserving the F-05 always-present contract).
- `GetResourceById` runs the per-id `MangaStatistics(id)` query via `FetchAndLinkMangaStatistics`.
- `MangaStatisticsResourceMapper.ToResource` (in `MangaResource.cs`) maps the model → DTO,
  filling the TV-shape aliases (`episodeCount` etc.) and `SeasonCount = 0`.

The progress-bar denominator (`ChapterCount`) is `Monitored OR HasFile` — Sonarr's air-date gate
is intentionally dropped (manga `FirstReleaseDate` is frequently NULL); the `Next/Previous/Last`
airing-date aggregates are not selected (no manga airing concept). See DIVERGENCE.md issue #335.
Coverage: `NzbDrone.Core.Test/MangaStatsTests/MangaStatisticsRepositoryFixture.cs` (SQL) +
`NzbDrone.Api.Test/Manga/MangaControllerStatisticsFixture.cs` (controller wiring).

## Key Files

### Phase 2 — core CRUD

| File | Purpose |
|------|---------|
| `MangaResource.cs` + `MangaResourceMapper` | DTO with singular cross-source IDs (`Guid? MangaDexId`, `int? MalId`, `int? AniListId`) per CONTEXT specifics. **quick-260618-eqz:** carries `userAlternativeTitles` (`List<string>?`) round-tripped BOTH directions — unlike the parser-internal metadata `AlternativeTitles` (deliberately NOT mapped), this IS user-owned: GET emits the stored list; PUT overwrites it. `ToModel` normalizes-on-write via `MangaTitleNormalizer.Normalize` + de-dupes, and applies the null-vs-empty guard (omitted field → null → ApplyChanges preserves; explicit `[]` → non-null empty → clears). Not added to the bulk `MangaEditorResource` (deferred). |
| `MangaController.cs` | GET/POST/PUT/DELETE `/api/v5/manga` (RestControllerWithSignalR) |
| `MangaLookupController.cs` | GET `/api/v5/manga/lookup?term=` — META-01 (primary metadata source via `IMetadataSourceFactory.GetPrimary`) |
| `StrayChaptersController.cs` + `StrayChapterPruneResource` | GET/POST `/api/v5/manga/{id}/straychapters` — maintenance surface for the `manga-removed-from-metadata-source` stray-outlier cleanup. The endpoint now classifies server-side via the WITH-FILE-anchored density cut (`StrayChapterPruneService` + `ChapterDensityCut`): the resource carries `densityCut` + `diskEvidenceAboveBaseline` and FOUR row lists — `fileLessStrays` (confident junk), `withFileStrays` (review), `uncertainStrays` (no on-disk anchor — needs a gateway search), `legitExtension` (dense real chapters past a stale metadata count — never pruned). `GET` is a read-only dry-run manifest; `POST` prunes — file-less confident junk always; with-file recycle-binned only on `?deleteFiles=true`; uncertain rows only on `?pruneUncertain=true`. Bare `Controller` + `[V5ApiController("manga")]` sharing the manga route prefix (mirrors `MangaFolderController`'s `{id}/folder`); backed by `IStrayChapterPruneService`. |
| `MangaLinksController.cs` | POST `/api/v5/manga/{id}/links` — manual relink per D-23 (NO auto-validation against secondary sources). Accepts ALL cross-source IDs — `MangaDexId`, `MalId`, `AniListId`, **`MangaBakaId`** (added for the v1.3 default-primary so a MangaDex-added manga can be relinked to MangaBaka — the remediation path `RefreshMangaService`'s skip-warning points at), plus the 5 exotic IDs (`KitsuId`/`AnimeNewsNetworkId`/`ShikimoriId`/`AnimePlanetId`/`MangaUpdatesId`). Collision-guarded (409) for the 4 primary-eligible IDs via `IMangaService.FindBy*Id` (incl. new `FindByMangaBakaId`); the exotic IDs are accepted verbatim (no finder). |

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
| `Wanted/MangaMissingController.cs` | GET `/api/v5/manga/wanted/missing` paged with monitored / mangaIds / languages / ageRating / `includeSubresources[]=Manga` filters (WANTED-01..03). Returns canonical `ChapterResource` (no custom paged-row DTO — mirrors TV `MissingController` reusing `EpisodeResource`; canonical-resource-reuse follow-up 2026-05-06 deleted the prior `MissingChapterResource` POCO). |
| `Wanted/MangaCutoffController.cs` | GET `/api/v5/manga/wanted/cutoff` paged with monitored / mangaIds / `includeSubresources[]=Manga` filters (Plan 12-12 F-CUTOFF closure). Returns canonical `ChapterResource` (canonical-resource-reuse follow-up 2026-05-06 deleted the prior `MangaCutoffResource` POCO; mirrors TV `CutoffController` reusing `EpisodeResource`). |
| `Wanted/MangaMissingSubresource.cs` + `Wanted/MangaCutoffSubresource.cs` | Enum-array subresource selectors driving the `[FromQuery] *Subresource[]? includeSubresources` query on the two wanted controllers (canonical-resource-reuse follow-up 2026-05-06; mirror TV `MissingSubresource { Series, Images }` + `CutoffSubresource { Series, EpisodeFile, Images }`). Each manga peer ships a single `Manga` value for v1. |

## Endpoints

### Phase 2 — core CRUD

- `GET    /api/v5/manga` — list all manga (covers mapped to local URLs)
- `GET    /api/v5/manga/{id}` — get one manga
- `POST   /api/v5/manga` — add a manga (delegates to `IAddMangaService.AddManga`; requires at least one of MangaDexId/MalId/AniListId/**MangaBakaId**). MangaBakaId was added to the anchor set because MangaBaka is the v1.3 default primary source and its catalog entries frequently carry no big-3 cross-link; `AddMangaService.PrepareForAdd` dedups MangaBaka-only adds via `FindByMangaBakaId`.
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
- `GET    /api/v5/manga/wanted/missing` — paged missing chapters; filters: `monitored=true&mangaIds[]&languages[]&ageRating&includeSubresources[]=Manga`. Returns canonical `ChapterResource` per canonical-resource-reuse follow-up (2026-05-06).
- `GET    /api/v5/manga/wanted/cutoff` — paged cutoff-unmet chapters (Plan 12-12 F-CUTOFF closure); filters: `monitored=true&mangaIds[]&includeSubresources[]=Manga`. Returns canonical `ChapterResource` per canonical-resource-reuse follow-up (2026-05-06).

## Patterns / Conventions

- **All endpoints `[V5ApiController]`** → admin X-Api-Key requirement (RESEARCH §Security Domain V4 — threat T-AUTHN-01).
- **`RestControllerWithSignalR<TResource, TModel>` + `IHandle<TEvent>`** for live-pushed entities (`MangaController` for the manga aggregate; `MangaQueueController` for the in-flight queue projection). The base type requires `where TModel : ModelBase, new()` — `MangaQueueItem` inherits `ModelBase` even though it is a pure projection POCO (Plan 06-09 Rule 2 deviation; mirrors TV `Queue` precedent).
- **`PagingRequestResource` query → `MapToPagingSpec` filter-expression chain**: every paged GET endpoint composes `pagingSpec.FilterExpressions.Add(c => ...)` calls based on optional query params; the BasicRepository pipeline runs them through the SqlBuilder.
- **`includeSubresources[]` query** drives optional Manga / Chapter hydration via service-layer `Get` calls at the controller layer (Plan 06-03 / 06-09 D-21 decision: repositories do NOT JOIN — controllers hydrate when asked).
- **Resource POCOs flatten entity → wire shape**: `MangaQueueResource` does not leak `RemoteChapter` (in-process EF reference); subresource POCOs (`MangaSubresource`, `ChapterSubresource`) carry only id + display fields.
- **`ICached<RemoteChapter>` round-trip between GET search and POST grab** (`MangaReleaseController`): keyed on `(IndexerId, Guid)` with 30-min TTL; mirrors TV `ReleaseController._remoteEpisodeCache`.
- **Wire-level RemoteEpisode shim for the manga grab path**: `MangaReleaseController.DownloadRelease` constructs `RemoteEpisode { Series = { Id = mangaId }, Episodes = [{ Id = chapterId }], Release = ... }` and calls `IMangaDownloadService.DownloadReport` (via the still-current `ToRemoteEpisodeShim()` bridge). **NOTE (Phase 39):** the original `InProcessImageDownloadClient` grab target was RETIRED in Phase 39 — `GatewayDownloadClient` is now the sole download client, so the Phase 4 D-10 `Protocol == DownloadProtocol.Http` in-process early-return no longer routes anywhere in-process. The historical narrative below describes the Phase 6 wiring before that retirement.
- **D-04 IsSynthetic-treated-identically pattern** at the REST layer: `MangaMissingController` does NOT add a `WHERE IsSynthetic = false` filter — synthetic rows (Phase 2 D-17 metadata-only-count fallback) are surfaced alongside real rows. A future `excludeSynthetic` query param could opt-in to the filter; v1 default is INCLUDE.
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

## Phase 13 — API V5 surface backfill

Phase 13 (API V5 Surface Audit) backfilled the controller-pair gaps surfaced by the
sub-wave A FINDINGS sweep ([13-API-V5-SURFACE-FINDINGS.md](../../../.planning/phases/13-api-v5-surface-audit/13-API-V5-SURFACE-FINDINGS.md))
under the D-13-04 forward-prophylactic disposition: when no manga peer existed AND no
D-13-05/06 exception applied, the manga peer ships regardless of current frontend usage.
Two new controllers landed at the `Manga/` directory root (one bulk-action editor, one
folder-name preview); peers under `Manga/Chapter/` and `Manga/Queue/` are documented in
those subdirectory CLAUDE.md files.

### Key Files (Phase 13 additions)

| File | Purpose |
|------|---------|
| `MangaEditorController.cs` | Bulk-action editor at `/api/v5/manga/editor` (Plan 13-04 — closes silent 404 from useManga.ts:608+:655 F-CUTOFF class) |
| `MangaEditorResource.cs` + `MangaEditorValidator.cs` | Bulk-edit DTO mirroring useManga.ts:470-477 wire shape (mangaIds + monitored? + translationProfileId? + customFormatProfileId? + rootFolderPath? + tags?); empty FluentValidation validator scaffold for v1 (preserves SeriesEditorController DI shape for Phase 15 collapse) |
| `MangaFolderController.cs` | Folder-name preview at `/api/v5/manga/{id}/folder` (Plan 13-05 — D-13-04 forward-prophylactic per Series-rename family rule) |

### Endpoints (Phase 13 additions)

- `PUT    /api/v5/manga/editor` — bulk apply field deltas (monitored, translationProfileId, customFormatProfileId, rootFolderPath, tags via ApplyTags switch); MoveFiles bool propagates to `IMangaService.UpdateManga`'s `useExistingRelativeFolder = !MoveFiles` arg
- `DELETE /api/v5/manga/editor` — bulk delete by mangaIds (with optional `deleteFiles` flag); per RESEARCH §Pitfall 4 the call drops the `addImportListExclusion` arg per `IMangaService.DeleteManga` 2-arg signature (Import Lists deferred to v1.1)
- `GET    /api/v5/manga/{id}/folder` — folder-name preview computed via `IBuildMangaFileNames.GetMangaFolder(manga, null)` (2-arg call per the manga-side divergence from TV's 1-arg `GetSeriesFolder`)

### Patterns / Conventions (Phase 13 additions)

- **Bulk-action controllers (`MangaEditorController`, `MangaFolderController`) extend bare `Controller`** (NOT `RestController<T>` or `RestControllerWithSignalR<,>`) per RESEARCH §Pitfall 1 — they do NOT broadcast SignalR; the underlying CRUD controller (`MangaController`) handles SignalR fan-out via its 11 `IHandle<*>` subscribers (see the table below). A future regression that "upgrades" either controller to a SignalR base would silently double-fire SignalR broadcasts on every bulk update.
- **`MangaFolderController` uses `[V5ApiController("manga")]`** (NOT `"manga/folder"`) per RESEARCH §Pitfall 2 — shares the route prefix with `MangaController` and discriminates via the action template `[HttpGet("{id}/folder")]`. Baking the suffix into the route attribute would produce `/api/v5/manga/folder/{id}/folder` and silently 404 every caller.
- **Allow-list DTO pattern (T-13-03 mass-assignment mitigation):** `MangaEditorResource` enumerates exactly the fields callers may flip; fields outside the whitelist are silently dropped by `System.Text.Json` deserialization. Empty `MangaEditorValidator` scaffold ships now to preserve the `SeriesEditorController` constructor-injection shape (easier Phase 15 collapse than retrofitting a validator dependency later).
- **Forward-compat no-op field:** `AddImportListExclusion` is preserved on `MangaEditorResource` for wire-shape stability with `useManga.ts:467` but dropped from the underlying `IMangaService.DeleteManga` call per RESEARCH §Pitfall 4 (manga signature is 2-arg only; Import Lists are deferred to v1.1 per PROJECT.md).
- **Phase 15 collapse:** Both `MangaEditorController` and `MangaFolderController` collapse with their TV peers (`SeriesEditorController` and `SeriesFolderController`) into a single canonical `EditorController` / `FolderController` when `Tv/` deletes per D-13-16.

### Cross-References (Phase 13)

- Sub-wave A FINDINGS (canonical source of truth for which controllers were backfilled): [13-API-V5-SURFACE-FINDINGS.md](../../../.planning/phases/13-api-v5-surface-audit/13-API-V5-SURFACE-FINDINGS.md)
- Phase 13 plan SUMMARYs:
  - [Plan 13-04 SUMMARY](../../../.planning/phases/13-api-v5-surface-audit/13-04-SUMMARY.md) — `MangaEditorController` + `MangaEditorResource` + `MangaEditorValidator`
  - [Plan 13-05 SUMMARY](../../../.planning/phases/13-api-v5-surface-audit/13-05-SUMMARY.md) — `MangaFolderController`
- Subdirectory CLAUDE.md updates:
  - [`Chapter/CLAUDE.md`](./Chapter/CLAUDE.md) — `RenameChapterController` + `ChapterFileController` (Plans 13-06 / 13-07)
  - [`Queue/CLAUDE.md`](./Queue/CLAUDE.md) — `MangaQueueDetailsController` + `MangaQueueStatusController` + `MangaQueueActionController` (Plans 13-08 / 13-09 / 13-10)

## Move Manga wire-up (issue #81 — 2026-05-13)

The backend MoveManga slice (`MoveMangaCommand` + `BulkMoveMangaCommand` + `MoveMangaService` + `MangaMovedEvent`) was shipped Phase 2 Plan 02-16 but had no controller publish site. Issue #81 closes the wire-up gap on both controllers:

| Controller | Method | Wire-up |
|------------|--------|---------|
| `MangaController` | `UpdateManga` (PUT `/api/v5/manga/{id}`) | Now accepts `[FromQuery] bool moveFiles = false`; if true, pushes `MoveMangaCommand` (with `trigger: CommandTrigger.Manual`) BEFORE the in-memory `ApplyChanges + UpdateManga` so `MoveMangaService` sees the original on-disk path. Mirrors upstream `SeriesController.UpdateSeries:198-212` verbatim. Ctor now injects `IManageCommandQueue`. |
| `MangaEditorController` | `SaveAll` (PUT `/api/v5/manga/editor`) | Now collects `List<BulkMoveManga>` inside the foreach when `resource.RootFolderPath.IsNotNullOrWhiteSpace()`; pushes `BulkMoveMangaCommand` if `resource.MoveFiles && mangaToMove.Any()`. Mirrors upstream `SeriesEditorController.SaveAll:60-66 + 90-100` verbatim. The existing `useExistingRelativeFolder = !resource.MoveFiles` arg on `IMangaService.UpdateManga` is preserved — it controls the in-memory path-builder branch so the new `Manga.Path` lands in the right shape regardless of whether the on-disk move runs. Stale file-header comment "No BulkMoveMangaCommand publish branch" refreshed to reflect the actual peer at `src/NzbDrone.Core/Manga/Commands/BulkMoveMangaCommand.cs`. |

**Live verification (mandatory per issue #81 acceptance criteria):** On-disk smoke test recorded in PR body — file hashes via `Get-FileHash` before move at `C:\tmp\mangarr-rfA\<title>\`; same hashes confirmed at `C:\tmp\mangarr-rfB\<title>\` after move; original directory removed by `TransferMode.Move`; DB `Manga.Path` + `Manga.RootFolderPath` updated. Idempotency: second move to same destination logs "is already in the specified location" with no file ops. Bulk-edit flow verified independently.

**Phase 15 collapse target:** `MoveMangaCommand` + `BulkMoveMangaCommand` + `MoveMangaService` collapse with their TV peers when `Tv/` deletes — Manga prefix dropped to canonical names.

## MangaController IHandle subscribers (post-Phase-10)

The `MangaController` fans out SignalR resource changes for the manga lifecycle. As of the Phase 10 close-out, the controller subscribes to **11 IHandle interfaces** — 4 from Phase 2 Plan 02-10, 1 from Phase 9 Plan 09-13, and 6 from Phase 10 sub-wave C (Plans 10-05 / 10-06 / 10-08). The `manga` SignalR resource name is auto-derived from `MangaResource.ResourceName` (Plan 07-02 + Plan 07-01 Lock #6); the frontend `SignalRListener.tsx` handler at line 399 invalidates `['/manga']` query key on every Updated / Created / Deleted action.

| #  | IHandle interface                  | Origin plan          | Behavior |
|----|------------------------------------|----------------------|----------|
| 1  | `IHandle<MangaAddedEvent>`         | Phase 2 Plan 02-10   | `BroadcastResourceChange(Created, message.Manga.Id)` |
| 2  | `IHandle<MangaUpdatedEvent>`       | Phase 2 Plan 02-10   | `BroadcastResourceChange(Updated, message.Manga.Id)` |
| 3  | `IHandle<MangaDeletedEvent>`       | Phase 2 Plan 02-10   | `BroadcastResourceChange(Deleted, MapResource(message.Manga))` (deleted-with-payload pattern; null-guarded) |
| 4  | `IHandle<ChapterListUpdatedEvent>` | Phase 2 Plan 02-10   | `BroadcastResourceChange(Updated, message.Manga.Id)` |
| 5  | `IHandle<MangaCoversUpdatedEvent>` | Phase 9 Plan 09-13   | `BroadcastResourceChange(Updated, message.Manga.Id)` IF `message.Updated == true` (skip on AlreadyExists short-circuit) |
| 6  | `IHandle<MangaEditedEvent>`        | **Phase 10 Plan 10-05** | `BroadcastResourceChange(Updated, message.Manga.Id)` — fires on user-explicit-edit (UI single-edit PUT after Plan 10-07's 3-arg `triggerSeriesEdited:true` opt-in + bulk-edit path) |
| 7  | `IHandle<MangaRenamedEvent>`       | **Phase 10 Plan 10-05** | `BroadcastResourceChange(Updated, message.Manga.Id)` — fires after `RenameChapterFileService` completes |
| 8  | `IHandle<MangaBulkEditedEvent>`    | **Phase 10 Plan 10-05** | `foreach (var manga in message.Manga) BroadcastResourceChange(Updated, manga.Id)` — bulk-edit fan-out, one broadcast per manga in payload |
| 9  | `IHandle<ChapterFileAddedEvent>`   | **Phase 10 Plan 10-06** | `BroadcastResourceChange(Updated, message.ChapterFile.MangaId)` — fires after chapter-file import (Plan 06-09 `ChapterFileService.Add` publishes; mangaId via direct FK property, not LazyLoad) |
| 10 | `IHandle<ChapterFileDeletedEvent>` | **Phase 10 Plan 10-06** | `BroadcastResourceChange(Updated, message.ChapterFile.MangaId)` — fires after chapter-file deletion. **Upgrade-reason short-circuit:** bails when `message.Reason == DeleteMediaFileReason.Upgrade` because the upcoming Add event will fire next; mirrors `SeriesController.Handle(EpisodeFileDeletedEvent)` verbatim. |
| 11 | `IHandle<MangaImportedEvent>`      | **Phase 10 Plan 10-08** | `foreach (var mangaId in message.MangaIds) BroadcastResourceChange(Updated, mangaId)` — bulk-add library-import fan-out, primitive-id payload (`List<int>`). Parallel subscriber: `MangaAddedHandler.IHandle<MangaImportedEvent>` (refresh-trigger PushMany — DryIoc dispatches both). |

**SignalR resource name:** `manga` (auto-derived from `MangaResource.ResourceName` via `RestControllerWithSignalR.cs:81-89`; Plan 07-02 `SignalRListener.tsx` handler entry verified).

**Phase 14 cleanup target:** This IHandle list collapses with `SeriesController.cs`'s IHandle list when `Tv/` deletes — Manga prefix dropped (e.g., `IHandle<MangaEditedEvent>` becomes `IHandle<EditedEvent>` in the renamed namespace). The 6 net-new Phase 10 entries are intentional manga-side parity additions; SeriesController retains the same 11-subscriber shape pre-cutover.

**Test coverage:** `src/NzbDrone.Api.Test/Manga/MangaControllerSignalRFixture.cs` (Plan 10-05 introduced; extended in Plans 10-06 + 10-08; total **7 tests** — 3 from Plan 10-05 covering Edited/Renamed/BulkEdited, 3 from Plan 10-06 covering ChapterFileAdded happy path + ChapterFileDeleted Manual-reason + ChapterFileDeleted Upgrade-reason `Times.Never` short-circuit, 1 from Plan 10-08 covering MangaImportedEvent bulk fan-out with `Times.Exactly(3)` assertion). All tests use predicate-locked `It.Is<SignalRMessage>(m => m.Action == ModelAction.Updated && m.Name == "manga")` shape — never `It.IsAny<>`. Full Api.Test suite green: 33/33.

**Note on test fixture location:** the fixture lives under `NzbDrone.Api.Test`, NOT `NzbDrone.Core.Test`, because `Mangarr.Core.Test` does not project-reference `Mangarr.Api.V5`. Plan 10-05 established this convention via Rule 3 deviation; Plans 10-06 + 10-08 appended to the same fixture file.

## Cross-References

- Mangarr V5 analogs (each Phase 6 controller mirrors a TV peer):
  - `src/Mangarr.Api.V5/Manga/History/ChapterHistoryController.cs` (history listing + retry)
  - `src/Mangarr.Api.V5/Manga/Blocklist/MangaBlocklistController.cs` (blocklist CRUD)
  - `src/Mangarr.Api.V5/Manga/Queue/MangaQueueController.cs` (in-flight queue + SignalR)
  - `src/Mangarr.Api.V5/Manga/Release/MangaReleaseController.cs` (Interactive Search + Grab)
  - `src/Mangarr.Api.V5/Manga/Wanted/MangaMissingController.cs` (Missing list)
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
- Phase 10 IHandle backfill SUMMARYs:
  - [Plan 10-05 SUMMARY](../../../.planning/phases/10-events-and-subscribers-sweep/10-05-SUMMARY.md) — MangaEditedEvent / MangaRenamedEvent / MangaBulkEditedEvent
  - [Plan 10-06 SUMMARY](../../../.planning/phases/10-events-and-subscribers-sweep/10-06-SUMMARY.md) — ChapterFileAddedEvent / ChapterFileDeletedEvent + Upgrade-reason short-circuit
  - [Plan 10-08 SUMMARY](../../../.planning/phases/10-events-and-subscribers-sweep/10-08-SUMMARY.md) — MangaImportedEvent bulk fan-out

---
*Last updated: 2026-05-05 after Phase 10 Plan 10-09 close-out — MangaController IHandle subscriber list refreshed from 5 to 11 entries (Plans 10-05/06/08 ship the source code; Plan 10-09 ships this doc update per CLAUDE.md HIGH PRIORITY rule).*
