# Manga/Queue (V5 API)

## Purpose

`/api/v5/manga/queue*` REST surface — in-flight queue projection (active downloads,
pending releases) plus the queue-action endpoints (grab, remove). The pre-existing
`MangaQueueController` (Phase 6 Plan 06-09) carries the canonical CRUD shape
(`RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>`); Phase 13 backfilled
the three peer controllers that mirror TV's `Queue/` directory layout
(`QueueDetailsController` + `QueueStatusController` + `QueueActionController`).

Sonarr divergence: NEW manga sibling of `src/Sonarr.Api.V5/Queue/`. Phase 15 cleanup
collapses the four manga controllers with their TV peers when `Tv/` deletes per
D-13-16 (purely additive in v1).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\Manga\Queue`

## Key Files

### Phase 6 — pre-existing CRUD baseline (extended in Phase 13 Plan 13-12)

| File | Purpose |
|------|---------|
| `MangaQueueController.cs` (Phase 6 Plan 06-09 — pre-existing; Plan 13-12 added `[HttpDelete("bulk")]` RemoveMany — F-01 gap closure from quick-260507-p13 smoke-test) | GET / DELETE / bulk-DELETE `/api/v5/manga/queue` (`RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>` with explicit `[V5ApiController("manga/queue")]` resource string + `IHandle<MangaQueueUpdatedEvent>` Sync broadcast). Bulk-DELETE iterates `resource.Ids` and calls `_queueService.Remove(id)` per element — mirrors TV peer `QueueController.cs:97-136` shape. Manga's simplified `Remove(int)` signature omits the v1 `blocklist/skipRedownload/changeCategory` params per the existing `[RestDeleteById]` precedent. |
| `MangaQueueResource.cs` (+ `MangaQueueResourceMapper`) | Queue DTO; flattens entity → wire shape; does not leak `RemoteChapter` (in-process EF reference); single-arg `ToResource()` extension shared with `MangaQueueDetailsController` |

### Phase 13 — API V5 surface backfill

| File | Purpose |
|------|---------|
| `MangaQueueDetailsController.cs` | GET `/api/v5/manga/queue/details` — paged queue+pending concat with optional Manga / Chapters subresource hydration (Plan 13-08). `RestControllerWithSignalR<MangaQueueResource, MangaQueueItem>` + dual `IHandle<MangaQueueUpdatedEvent>` + `IHandle<MangaPendingReleasesUpdatedEvent>` Sync fan-out (mirrors TV `QueueDetailsController` verbatim with manga services). |
| `MangaQueueSubresource.cs` | Enum-array subresource selector `{ Manga, Chapters }` driving the `[FromQuery] MangaQueueSubresource[]? includeSubresources` query on `MangaQueueDetailsController` (Plan 13-08; mirror of TV `QueueSubresource { Series, Episodes }` shape). Plural `Chapters` mirrors TV's plural `Episodes`. |
| `MangaQueueStatusController.cs` + `MangaQueueStatusResource.cs` | GET `/api/v5/manga/queue/status` — debounced 5s broadcast queue counters (Plan 13-09). `RestControllerWithSignalR<MangaQueueStatusResource, MangaQueueItem>` + dual `IHandle<MangaQueueUpdatedEvent>` + `IHandle<MangaPendingReleasesUpdatedEvent>` SignalR fan-out via `Debouncer(TimeSpan.FromSeconds(5))`. Counter DTO: `TotalCount` + `Count` + `UnknownCount` + `Errors` + `Warnings` + `UnknownErrors` + `UnknownWarnings`. `[NonAction]` shadow on `GetResourceByIdWithErrorHandler` (queue is push-only via Sync — no by-id read path). |
| `MangaQueueActionController.cs` | POST `/api/v5/manga/queue/grab/{id:int}` + POST `/api/v5/manga/queue/grab/bulk` (Plan 13-10). Bare `Controller` (NOT `RestControllerWithSignalR`); shares the `[V5ApiController("manga/queue")]` route prefix with `MangaQueueController` per route prefix coexistence (RESEARCH §Pitfall 6) — no collision because action templates discriminate. Bridges manga DTO into the existing TV-shaped `IDownloadService.DownloadReport(RemoteEpisode, int?)` contract via `RemoteChapter.ToRemoteEpisodeShim()` (Phase 6 Plan 06-09 shim; Phase 15 collapse drops it). |

## Endpoints

### Phase 6 — pre-existing CRUD baseline (extended in Phase 13 Plan 13-12)

```
GET    /api/v5/manga/queue                                        Full queue projection (no DB paging — static-list projection from TrackedDownloadRefreshedEvent)
DELETE /api/v5/manga/queue/{id}                                   Remove an in-flight item
DELETE /api/v5/manga/queue/bulk                                   Bulk-remove in-flight items (body: { ids: [...] } via TV's domain-neutral QueueBulkResource) — Plan 13-12 F-01 gap closure (smoke-test quick-260507-p13)
```

### Phase 13 — API V5 surface backfill

```
GET    /api/v5/manga/queue/details?mangaId={id}&chapterIds={ids}&includeSubresources[]=Manga,Chapters    Paged queue+pending concat with optional id-filter and subresource hydration (Plan 13-08)
GET    /api/v5/manga/queue/status                                                                        Debounced 5s broadcast counter resource (Plan 13-09)
POST   /api/v5/manga/queue/grab/{id:int}                                                                 Grab a single pending release (Plan 13-10)
POST   /api/v5/manga/queue/grab/bulk                                                                     Grab many pending releases (body: { ids: [...] } via TV's domain-neutral QueueBulkResource) (Plan 13-10)
```

## Patterns / Conventions

### Route prefix coexistence (RESEARCH §Pitfall 6)

`MangaQueueController` (existing, `"manga/queue"`) + `MangaQueueDetailsController`
(`"manga/queue/details"`) + `MangaQueueStatusController` (`"manga/queue/status"`) +
`MangaQueueActionController` (`"manga/queue"` — shared with `MangaQueueController`)
all coexist. ASP.NET Core MVC routes by `[Http*]` action templates, so no collision:

| Controller | Route attribute | Action templates |
|------------|-----------------|------------------|
| `MangaQueueController` | `[V5ApiController("manga/queue")]` | bare CRUD verbs (`GET /`, `DELETE /{id}`) inherited from `RestControllerWithSignalR<TResource, TModel>` base + explicit `[HttpDelete("bulk")]` (Plan 13-12 F-01 gap closure — bulk DELETE belongs on the CRUD controller per TV peer `QueueController.cs:97-136`, NOT on `MangaQueueActionController`) |
| `MangaQueueDetailsController` | `[V5ApiController("manga/queue/details")]` | bare GET (paged) inherited from base |
| `MangaQueueStatusController` | `[V5ApiController("manga/queue/status")]` | bare GET inherited from base |
| `MangaQueueActionController` | `[V5ApiController("manga/queue")]` (shared with `MangaQueueController`) | `[HttpPost("grab/{id:int}")]` + `[HttpPost("grab/bulk")]` (the `:int` constraint + `grab/*` action template is the discriminator) |

A future regression that adds an unconstrained `[HttpPost("grab")]` or a bare `[HttpGet("")]`
on `MangaQueueActionController` would collide with `MangaQueueController`'s inherited GET —
the Wave 0 fixture pin (`Action_template_is_grab_id_int_per_PATTERNS`) catches this before
runtime route discovery silently picks one of the two. The Plan 13-12 fixture
(`Action_template_is_HttpDelete_bulk` in `MangaQueueControllerBulkDeleteFixture`) plays the
same protective role for the new `[HttpDelete("bulk")]` action template.

### Routing home for bulk DELETE (Plan 13-12)

Bulk DELETE belongs on `MangaQueueController` (the CRUD controller), NOT on
`MangaQueueActionController` (the bare-Controller bulk-action peer). This mirrors the TV
peer convention: `QueueController.cs:97-136` carries `[HttpDelete("bulk")]` RemoveMany,
while `QueueActionController` ships only `[HttpPost("grab/{id:int}")]` + `[HttpPost("grab/bulk")]`.
Plan 13-10's stated objective referenced the URL pattern but correctly omitted the action
from `MangaQueueActionController`; smoke-test `quick-260507-p13` finding F-01 surfaced the
missing route a phase later, and Plan 13-12 closed it on the CRUD controller per the canonical
Sonarr pattern.

### Subresource hydration

`MangaQueueDetailsController` gates subresource projection AFTER mapping (a private
`Project` method null-projects `Manga` / `Chapter` / `ChapterIds` when the include-flag
is false). This semantically mirrors TV `QueueDetailsController`'s gate-BEFORE-mapping
two-arg `ToResource` extension — but the existing single-arg `MangaQueueResourceMapper.ToResource`
is shared with `MangaQueueController`, and extending it with include-flags would force a
mapper signature change rippling through that controller. The post-mapping null-projection
preserves both the wire-shape contract and the shared-mapper invariant.

### SignalR push-only via Sync (queue + status)

Both `MangaQueueDetailsController` and `MangaQueueStatusController` override
`GetResourceByIdWithErrorHandler` with `[NonAction]` (queue is push-only via Sync
broadcasts — no by-id read path). `GetResourceById` is NOT overridden (the inherited
virtual returns null, which the SignalR base's `BroadcastResourceChange(action, id)`
path tolerates via the `Action == Deleted` short-circuit). Mirrors TV peers
`QueueDetailsController.cs:35-38` and `QueueStatusController.cs:32-36` verbatim.

### Debounced status broadcast (Plan 13-09)

`MangaQueueStatusController` uses `NzbDrone.Common.TPL.Debouncer(TimeSpan.FromSeconds(5))`
to batch queue-mutation broadcasts and prevent SignalR spam during high-frequency
`TrackedDownloadRefreshedEvent` bursts. Pause/Resume guard around the GET read so
synchronous client polling does not trigger another broadcast (mirrors
`QueueStatusController.cs:42-58` verbatim).

### Manga discriminator on counters (Plan 13-09)

`MangaQueueItem.MangaId` is `int?` (Plan 06-09 projection POCO), so the counter routing
discriminator is `q.MangaId.HasValue && q.MangaId.Value > 0` (NOT `q.Manga != null` like
TV peer — TV's `Queue` carries a navigation property; manga's projection POCO carries the
FK directly). `TrackedDownloadStatus` comparison uses string literals `"Error"` / `"Warning"`
because `MangaQueueItem.TrackedDownloadStatus` is a string populated by `td.Status.ToString()`
in `MangaQueueService.MapQueueItem:149`.

### Queue-action shim bridge (Plan 13-10)

`MangaQueueActionController.GrabRelease` calls `IMangaPendingReleaseService.FindPendingQueueItem`
to locate the pending release, then calls `IDownloadService.DownloadReport(pendingRelease.RemoteChapter.ToRemoteEpisodeShim(), null)`.
The `RemoteChapter.ToRemoteEpisodeShim()` bridge (Phase 6 Plan 06-09 shim) re-shapes the
manga DTO into the TV-shaped `RemoteEpisode` that `IDownloadService.DownloadReport`
expects. Phase 15 D-15-XX type unification will introduce a unified
`IDownloadService.DownloadReport(RemoteChapter, int?)` overload and the shim disappears.

### Threat mitigations

- **T-13-01 (silent misroute on hybrid grab)** — Plan 13-10 ships dedicated
  `MangaQueueActionController` so `useQueue.ts:178/:205` manga callers resolve to the
  manga-shape pipeline (RemoteChapter → shim) instead of silently hitting the TV
  `QueueActionController.GrabRelease` which assumed TV `RemoteEpisode` shape.
- **T-13-02 (Spoofing — distinct SignalR resources)** — `manga/queue/details` and
  `manga/queue/status` are distinct SignalR resource names from `manga/queue`. Plan 13-99
  close-out re-runs Plan 13-00 Pattern κ check; if either is flagged orphan-controller
  (no `SignalRListener.tsx` handler), a follow-up plan adds the handler entries.
- **T-13-03 (Tampering — Mass-assignment via subresource enum)** — `MangaQueueSubresource`
  is a C# enum (compile-time enforced); ASP.NET Core model binder rejects unknown enum
  values during query-string binding.
- **T-13-04 (Information Disclosure)** — All endpoints inherit `[V5ApiController]`
  admin X-Api-Key requirement (RESEARCH §Security Domain V4).
- **T-13-05 (Tampering on bulk)** — `MangaQueueActionController` reuses TV's domain-neutral
  `QueueBulkResource` (`{ Ids : int[] }`); `[Consumes("application/json")]` + DTO-with-no-extras
  rejects unknown fields.

## Manga Adaptation Notes

- **Phase 15 collapse:** When `Tv/` deletes per D-13-16, all four manga queue controllers
  collapse with their TV peers under a unified `Queue/` namespace:
  - `MangaQueueController` → drop manga prefix, becomes `QueueController`
  - `MangaQueueDetailsController` → becomes `QueueDetailsController`
  - `MangaQueueStatusController` → becomes `QueueStatusController`
  - `MangaQueueActionController` → becomes `QueueActionController`
  - `MangaQueueSubresource` → collapses with `QueueSubresource`
  - `RemoteChapter.ToRemoteEpisodeShim()` call site disappears alongside the unified
    `IDownloadService.DownloadReport(RemoteChapter, int?)` overload.
- **Frontend re-pointing (partial — 2026-05-09):** `QueueDetailsProvider.tsx` was repointed
  onto `/manga/queue/details` by the home-404s-queue-qualityprofile fix; filter params
  mapped (`seriesId`→`mangaId`, `episodeIds`→`chapterIds`); the legacy `all=true`
  discriminator dropped (manga endpoint returns the full queue with no filter); helper
  hooks (`useQueueDetailsForSeries` etc.) fall back to manga-shape fields. Still deferred:
  `useQueueStatus.ts:15` (`/queue/status`) — separate plan per D-13-16 additive-only mandate.

## Test fixture location

Per the same Plan 10-05 Rule 3 deviation, queue-controller fixtures live under
`NzbDrone.Api.Test` (NOT `NzbDrone.Core.Test`) because `Sonarr.Core.Test` does not
project-reference `Sonarr.Api.V5`; `Sonarr.Api.Test` does. Phase 13 fixtures:

- `src/NzbDrone.Api.Test/Manga/Queue/MangaQueueDetailsControllerFixture.cs` (Plan 13-08; 4 tests)
- `src/NzbDrone.Api.Test/Manga/Queue/MangaQueueStatusControllerFixture.cs` (Plan 13-09; 4 tests)
- `src/NzbDrone.Api.Test/Manga/Queue/MangaQueueActionControllerFixture.cs` (Plan 13-10; 5 tests)
- `src/NzbDrone.Api.Test/Manga/Queue/MangaQueueControllerBulkDeleteFixture.cs` (Plan 13-12; 3 tests — F-01 gap closure for the new `[HttpDelete("bulk")]` RemoveMany action)

## Cross-References

- Sub-wave A FINDINGS (canonical source of truth for Phase 13 backfills): [13-API-V5-SURFACE-FINDINGS.md](../../../../.planning/phases/13-api-v5-surface-audit/13-API-V5-SURFACE-FINDINGS.md)
- Phase 13 plan SUMMARYs:
  - [Plan 13-08 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-08-SUMMARY.md) — `MangaQueueDetailsController` + `MangaQueueSubresource`
  - [Plan 13-09 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-09-SUMMARY.md) — `MangaQueueStatusController` + `MangaQueueStatusResource`
  - [Plan 13-10 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-10-SUMMARY.md) — `MangaQueueActionController`
  - [Plan 13-12 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-12-SUMMARY.md) — `MangaQueueController.RemoveMany` (`[HttpDelete("bulk")]`) F-01 gap closure
- TV peers (Phase 15 collapse targets):
  - `src/Sonarr.Api.V5/Queue/QueueController.cs`
  - `src/Sonarr.Api.V5/Queue/QueueDetailsController.cs`
  - `src/Sonarr.Api.V5/Queue/QueueStatusController.cs`
  - `src/Sonarr.Api.V5/Queue/QueueActionController.cs`
- Backing services (Phase 6/9):
  - [`IMangaQueueService`](../../../NzbDrone.Core/Queue/Manga/IMangaQueueService.cs) — Plan 06-05 / 06-09
  - [`IMangaPendingReleaseService`](../../../NzbDrone.Core/Download/Pending/Manga/IMangaPendingReleaseService.cs) — Plan 09-10
  - [`MangaQueueUpdatedEvent` + `MangaPendingReleasesUpdatedEvent`](../../../NzbDrone.Core/Queue/Manga/MangaQueueUpdatedEvent.cs)
- Manga V5 root: [`../CLAUDE.md`](../CLAUDE.md) — Phase 2 + Phase 6 + Phase 13 sibling endpoints
- V5 API root: [`../../CLAUDE.md`](../../CLAUDE.md)
