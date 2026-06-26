# NzbDrone.Core/Queue

## Purpose

Live in-flight queue projection — the read-model that the UI Activity panel and the Decision Engine's `QueueSpecification` consume. Built on top of `Download/TrackedDownloads/` (the source of truth for in-flight downloads) by the **static-list projection pattern**: a single `IHandle<TrackedDownloadRefreshedEvent>` rebuilds the projection atomically; readers defensive-copy via `ToList()`.

The TV `QueueService.cs` / `Queue.cs` / `ObsoleteQueueService.cs` family was **DELETED in the Phase 15 fork** (V3 API + TV `Tv/` cutover). `MangaQueueService` (under `Queue/Manga/`) is now the **sole** `IHandle<TrackedDownloadRefreshedEvent>` projection — HEAD-verified Phase 36 Plan 02 (no TV `QueueService.cs`/`Queue.cs` on disk; only `MangaQueueService` subscribes; no TV `QueueController` in `Mangarr.Api.V5`). There is no second queue and no double-counting risk; the original Phase 6 `Protocol == DownloadProtocol.Http` split (described below) is now a harmless heritage guard rather than a live co-existence requirement.


## Top-Level Files

| File | Purpose |
|------|---------|
| `QueueStatus.cs` | Derived status enum (`Downloading / Completed / Failed / Warning / Paused / Queued / DelayedSearchPending / …`) — shared, manga-consumed. |
| `QueueUpdatedEvent.cs` | Marker `IEvent` (heritage TV-side type; the manga projection emits `MangaQueueUpdatedEvent` from `Queue/Manga/`). |
| `DatetimeComparer.cs` / `TimeleftComparer.cs` | Sort helpers for the projection. |

> **DELETED in Phase 15 (do NOT expect these on disk):** `Queue.cs` (TV-shaped row POCO),
> `QueueService.cs` (TV `IHandle<TrackedDownloadRefreshedEvent>` projection), and
> `ObsoleteQueueService.cs` / `ObsoleteQueueUpdatedEvent.cs` (deprecated V3-compat pre-projection
> shape). They were removed with the `Tv/` cutover and V3 API deletion. Reference the upstream
> `v5-develop` `QueueService` when porting shape. HEAD-verified Phase 36 Plan 02
> (see `36-RESEARCH.md` § "Landmine #2 — ALREADY NEUTRALIZED").

## Phase 6 Manga Sibling

Phase 6 Plan 06-05 shipped `Queue/Manga/` as a sibling of the (now-deleted) TV `Queue` family. Because the TV projection no longer exists, `Queue/Manga/MangaQueueService` is the **only** live projection; the planned "Phase 8 collapse" (drop the `Protocol == Http` filter and rename out the `Manga` prefix) is the remaining cosmetic cleanup, not a behavioral fix — there is no longer a second projection to merge.

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`Queue/Manga/`](./Manga/CLAUDE.md) | Plan 06-05 | Manga queue projection from `TrackedDownloadRefreshedEvent`. `MangaQueueItem` POCO (inherits `ModelBase` per Plan 06-09 Rule 2 — `RestControllerWithSignalR` requires the constraint); `IMangaQueueService` + `MangaQueueService` static-list projection filtered to `Protocol == DownloadProtocol.Http` (originally to let the now-deleted TV queue co-exist on the same event without double-counting — now a harmless heritage guard since `MangaQueueService` is the sole subscriber); `MangaQueueUpdatedEvent` IEvent marker (SignalR fan-out trigger consumed by Plan 06-09 `MangaQueueController`); `TrackedDownload.RemoteChapter` additive optional slot (parallel to `RemoteEpisode`, populated on the manga-protocol path by the Phase-36 monitoring loop / `GatewayDownloadClient`; the Phase-4 `InProcessImageDownloadClient` that originally populated it was retired in Phase 39). Drops TV-only `Languages` / `QualityModel`; adds `TranslatedLanguage : string` (BCP-47) + `ScanlationGroup : string` as first-class fields. Deterministic `HashConverter.GetHashInt31` Id (SignalR diff key). Phase 8 cleanup: collapse with `QueueService`. |
| `Queue/Manga/MangaPendingReleasesUpdatedEvent.cs` (defined alongside `MangaQueueUpdatedEvent.cs`) | Plan 06-05 | Reserved for the Plan 06-08 auto-retry orchestrator hand-off path. Phase 8 cleanup: collapse with `PendingReleasesUpdatedEvent`. |

**Phase 6 D-20 GUARD:** the manga `QueueDuplicateSpecification` (in `DecisionEngine/Manga/Specifications/`) intersects `subjectChapterIds ∪ queuedChapterIds` via `HashSet<int>.Overlaps` against `IMangaQueueService.GetMangaQueue()` — NEVER `IQueueService.GetQueue()`. The two services serve different rows.

## Manga Adaptation Notes

The static-list projection pattern is the canonical Mangarr shape and is preserved verbatim by Mangarr (Phase 6 Q-3 RESEARCH lock). The two divergences are:
1. **Filter on Protocol** (`Protocol == DownloadProtocol.Http`) — originally so the TV and manga projections did not double-count rows from the shared `TrackedDownloadRefreshedEvent`. The TV projection was deleted in Phase 15, so this is now a harmless heritage guard (defensive against any future non-Http row), NOT a live co-existence requirement.
2. **Drop Quality / Languages**, add **TranslatedLanguage / ScanlationGroup** as first-class fields.

The only remaining "Phase 8 collapse" work is cosmetic — drop the `Protocol == Http` filter and rename out the `Manga` prefix — since the TV `QueueService` it would have merged with no longer exists.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — `TrackedDownloads/` is the upstream source
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `QueueSpecification` (TV) + `QueueDuplicateSpecification` (manga) consume the projections
- [../../Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) — `Manga/Queue/MangaQueueController.cs` exposes the projection over REST + SignalR
