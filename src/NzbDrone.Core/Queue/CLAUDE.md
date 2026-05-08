# NzbDrone.Core/Queue

## Purpose

Live in-flight queue projection — the read-model that the UI Activity panel and the Decision Engine's `QueueSpecification` consume. Built on top of `Download/TrackedDownloads/` (the source of truth for in-flight downloads) by the **static-list projection pattern**: a single `IHandle<TrackedDownloadRefreshedEvent>` rebuilds the projection atomically; readers defensive-copy via `ToList()`.

The TV `QueueService` projects all `TrackedDownload` rows; Mangarr Phase 6 splits the projection along `DownloadProtocol` so manga and TV co-exist on the same upstream event without double-counting.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Queue\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `Queue.cs` | TV-shaped queue row — wire-shape POCO inheriting `ModelBase`. Carries `Series + Episodes + Quality + Languages + Status + RemainingTime + …`. |
| `QueueService.cs` | TV-shaped `IHandle<TrackedDownloadRefreshedEvent>` static-list projection (lines 14-106). Verbatim shape template referenced by Phase 6 Plan 06-05. |
| `QueueStatus.cs` | Derived status enum (`Downloading / Completed / Failed / Warning / Paused / Queued / DelayedSearchPending / …`) |
| `QueueUpdatedEvent.cs` | TV-side marker `IEvent` published by `QueueService.Handle` on every refresh; SignalR fan-out trigger. |
| `ObsoleteQueueService.cs` / `ObsoleteQueueUpdatedEvent.cs` | Deprecated TV pre-projection shape; kept for API V3 compatibility. |
| `DatetimeComparer.cs` / `TimeleftComparer.cs` | Sort helpers for the projection. |

## Phase 6 Manga Sibling

Phase 6 Plan 06-05 ships `Queue/Manga/` as a parallel sibling. **Phase 8 cleanup** will collapse with `Queue/` when `Tv/` deletes (and the `Protocol == Http` filter drops along with the TV-side projection).

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`Queue/Manga/`](./Manga/CLAUDE.md) | Plan 06-05 | Manga queue projection from `TrackedDownloadRefreshedEvent`. `MangaQueueItem` POCO (inherits `ModelBase` per Plan 06-09 Rule 2 — `RestControllerWithSignalR` requires the constraint); `IMangaQueueService` + `MangaQueueService` static-list projection filtered to `Protocol == DownloadProtocol.Http` (so TV and manga queues co-exist on the same upstream event without double-counting); `MangaQueueUpdatedEvent` IEvent marker (SignalR fan-out trigger consumed by Plan 06-09 `MangaQueueController`); `TrackedDownload.RemoteChapter` additive optional slot (parallel to `RemoteEpisode`, populated by Phase 4 `InProcessImageDownloadClient` on the manga-protocol path). Drops TV-only `Languages` / `QualityModel`; adds `TranslatedLanguage : string` (BCP-47) + `ScanlationGroup : string` as first-class fields. Deterministic `HashConverter.GetHashInt31` Id (SignalR diff key). Phase 8 cleanup: collapse with `QueueService`. |
| `Queue/Manga/MangaPendingReleasesUpdatedEvent.cs` (defined alongside `MangaQueueUpdatedEvent.cs`) | Plan 06-05 | Reserved for the Plan 06-08 auto-retry orchestrator hand-off path. Phase 8 cleanup: collapse with `PendingReleasesUpdatedEvent`. |

**Phase 6 D-20 GUARD:** the manga `QueueDuplicateSpecification` (in `DecisionEngine/Manga/Specifications/`) intersects `subjectChapterIds ∪ queuedChapterIds` via `HashSet<int>.Overlaps` against `IMangaQueueService.GetMangaQueue()` — NEVER `IQueueService.GetQueue()`. The two services serve different rows.

## Manga Adaptation Notes

The static-list projection pattern is the canonical Mangarr shape and is preserved verbatim by Mangarr (Phase 6 Q-3 RESEARCH lock). The two divergences are:
1. **Filter on Protocol** so TV and manga don't double-count rows from the shared `TrackedDownloadRefreshedEvent`.
2. **Drop Quality / Languages**, add **TranslatedLanguage / ScanlationGroup** as first-class fields.

Phase 8 collapses the two services into one when `Tv/` deletes (and the Http filter drops with it).

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — `TrackedDownloads/` is the upstream source
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `QueueSpecification` (TV) + `QueueDuplicateSpecification` (manga) consume the projections
- [../../Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) — `Manga/Queue/MangaQueueController.cs` exposes the projection over REST + SignalR
