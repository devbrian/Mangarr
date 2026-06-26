# Queue/Manga

## Purpose

Phase 6 D-20 manga sibling of `src/NzbDrone.Core/Queue/` (the TV `Queue` family that was **deleted in the Phase 15 fork**). Ships the `MangaQueueService` static-list projection that fans the `IHandle<TrackedDownloadRefreshedEvent>` lifecycle into a manga-shaped `List<MangaQueueItem>` filtered to `DownloadProtocol.Http` entries, and emits `MangaQueueUpdatedEvent` on every refresh. Wires the Phase 5 `QueueDuplicateSpecification` STUB (D-20) so the decision engine can ask "is this chapter already in flight?" instead of accepting every release. As of the Phase 15 fork this is the **sole** `IHandle<TrackedDownloadRefreshedEvent>` projection (HEAD-verified Phase 36 Plan 02 — no TV `QueueService.cs`/`Queue.cs` on disk, no TV `QueueController` in V5).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Queue\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaQueueItem.cs` | POCO row. Wire-shape sibling of the deleted TV `Queue/Queue.cs` (reference `v5-develop` for the original shape). Drops TV-only `Languages` / `QualityModel`; adds `TranslatedLanguage` + `ScanlationGroup` as first-class fields (Phase 3 D-Q4 wire shape). Carries `RemoteChapter` so `QueueDuplicateSpecification` can intersect on `Chapter.Id` lists. `Id` is computed via `HashConverter.GetHashInt31` over `"trackedDownload-{client}-{downloadId}-{chapterId}"` so the same projection re-issues the same `Id` across refreshes (SignalR diff key). |
| `MangaQueueUpdatedEvent.cs` | Marker `IEvent`. Emitted by `MangaQueueService.Handle` on every successful projection refresh. Plan 06-09 V5 controller subscribes via SignalR to fan diffs out to the React Activity panel. Also defines `MangaPendingReleasesUpdatedEvent` (sibling of `PendingReleasesUpdatedEvent`) reserved for Plan 06-08 auto-retry orchestration. |
| `IMangaQueueService.cs` | Contract: `GetMangaQueue() : List<MangaQueueItem>`, `Find(int) : MangaQueueItem`, `Remove(int) : void`. Mirrors `IQueueService` verbatim — only the row type is manga-specific. |
| `MangaQueueService.cs` | Service. The **sole** `IHandle<TrackedDownloadRefreshedEvent>` static-list projection (Q-3 RESEARCH lock — single-writer / process-wide pattern ported from Sonarr's `QueueService`, which was deleted in Phase 15). Filters to `t.IsTrackable && t.Protocol == DownloadProtocol.Http` — originally the gate that let the now-deleted TV queue co-exist on the same event without double-counting, now a harmless heritage guard. Maps one row per `RemoteChapter.Chapters` entry; emits a single shell row when `RemoteChapter` is null (orphan recovery path). |

## Patterns / Conventions

### Static-list projection on TrackedDownloadRefreshedEvent (Q-3 RESEARCH lock)

```csharp
public class MangaQueueService : IMangaQueueService, IHandle<TrackedDownloadRefreshedEvent>
{
    private static List<MangaQueueItem> _queue = new();   // process-wide projection

    public void Handle(TrackedDownloadRefreshedEvent message)
    {
        _queue = message.TrackedDownloads
            .Where(t => t.IsTrackable && t.Protocol == DownloadProtocol.Http)
            .OrderBy(c => c.DownloadItem?.RemainingTime ?? TimeSpan.MaxValue)
            .SelectMany(MapQueueItems)
            .ToList();

        _eventAggregator.PublishEvent(new MangaQueueUpdatedEvent());
    }
}
```

The static field is the same anti-pattern Sonarr's `QueueService` used (ported verbatim) and has been stable through five major versions. T-06-10 (concurrent-write race) is mitigated by a **static `_queueLock` monitor** (Phase 6 Plan 14 BL-02): all `_queue` access serializes through it. The original single-writer assumption no longer holds — `Remove(int id)` (the `DELETE /api/v5/manga/queue/{id}` path) mutates `_queue` in place via `List.Remove`, so it is a second writer alongside the `IHandle` reassignment. The lock is `static` so it matches the lifetime of the `static _queue` field — DI-transient service instances still serialize through one monitor. `Handle` builds the new projection OUTSIDE the lock (mutation-free LINQ) and only the reassignment is inside the critical section; reads (`GetMangaQueue`, `Find`) and `Remove` take the same lock and `GetMangaQueue` defensive-copies via `ToList()` so iteration during a refresh cannot tear. `Remove` republishes `MangaQueueUpdatedEvent` so connected SignalR clients see the deletion without waiting for the next `TrackedDownloadRefreshedEvent`.

### Protocol filter — now a heritage guard (TV queue deleted)

`TrackedDownloadRefreshedEvent` is the shared upstream lifecycle event. In the original Phase 6 design it carried TV (`Usenet` / `Torrent`) AND manga (`Http`) entries, and TWO services subscribed — the TV `QueueService` (all rows) and `MangaQueueService` (Http-filtered) — so the protocol filter kept them from double-counting. **The TV `QueueService` was deleted in the Phase 15 fork**, so `MangaQueueService` is now the only subscriber (HEAD-verified Phase 36 Plan 02):

| Service | Filter | Notes |
|---------|--------|-------|
| ~~`QueueService` (TV)~~ | — | **DELETED in Phase 15** (no `QueueService.cs`/`Queue.cs` on disk). |
| `MangaQueueService` (manga) | `t.Protocol == DownloadProtocol.Http` | The sole subscriber. The filter is now a harmless heritage guard (defensive against any future non-Http row), NOT a co-existence requirement. **No double-projection risk** when the `TrackedDownloadRefreshedEvent` publisher is (re)wired. |

Remaining "Phase 8 collapse" work is cosmetic — drop the `Protocol == Http` filter and rename out the `Manga` prefix; there is no second queue left to merge.

### Deterministic Id (SignalR diff key)

```csharp
item.Id = HashConverter.GetHashInt31($"trackedDownload-{item.DownloadClient}-{item.DownloadId}-{chapter?.Id ?? 0}");
```

Same `(downloadClient, downloadId, chapterId)` triple → same `Id` across refreshes. The V5 controller (Plan 06-09) ships these via SignalR; React's `Activity/Queue` panel uses `Id` as the React `key={...}` for animated transitions. Without determinism, every refresh would re-render the entire list.

### One row per chapter (multi-chapter releases)

Manga releases can pack multiple chapters (e.g., a "v3 c14-16" volume archive). The projection emits one `MangaQueueItem` per chapter so `QueueDuplicateSpecification` can intersect on `Chapter.Id` lists without false-negatives:

```csharp
private IEnumerable<MangaQueueItem> MapQueueItems(TrackedDownload trackedDownload)
{
    var rc = trackedDownload.RemoteChapter;
    if (rc == null || rc.Chapters == null || rc.Chapters.Count == 0)
    {
        yield return MapQueueItem(trackedDownload, null);   // shell row — orphan recovery
        yield break;
    }
    foreach (var chapter in rc.Chapters)
    {
        yield return MapQueueItem(trackedDownload, chapter);
    }
}
```

### STUB body replacement preserves auto-discovery count

`QueueDuplicateSpecification` gains an `IMangaQueueService` constructor dependency but the 11-spec auto-discovery count (F-01 fixture `Be(11)`) remains stable because the `IMangaDecisionEngineSpecification` implementation is unchanged. Pitfall 6 GUARD: `AutoMoqer`-style fixtures consuming the spec MUST register `IMangaQueueService` or the spec drops from `IEnumerable<>` resolution. Local fixtures in this plan inject the mock directly.

### TrackedDownload.RemoteChapter slot (Phase 6 additive)

`TrackedDownload` gained an optional `RemoteChapter` slot in this plan parallel to the pre-existing `RemoteEpisode`. It is populated on the manga-protocol path by the Phase-36 monitoring loop / `GatewayDownloadClient` (the Phase-4 `InProcessImageDownloadClient` that originally populated it was retired in Phase 39), so the projection has a real row to map.

## Manga Adaptation Notes

### TV-vs-manga mapping

| TV (Mangarr) | Manga (Mangarr) | Difference |
|-------------|-----------------|------------|
| `Queue.Series` | `MangaQueueItem.Manga` | Aggregate root rename |
| `Queue.Episodes : List<Episode>` | `MangaQueueItem.Chapters : List<Chapter>` + `MangaQueueItem.Chapter` (singular per row) | Multi-chapter release projects N rows |
| `Queue.Languages : List<Language>` + `Queue.Quality : QualityModel` | `MangaQueueItem.TranslatedLanguage : string` (BCP-47) | Phase 5 D-04 — manga has no quality model. `Languages` enum is TV-region-flavored. |
| `Queue.RemoteEpisode` | `MangaQueueItem.RemoteChapter` | DTO rename |
| `QueueUpdatedEvent` | `MangaQueueUpdatedEvent` | Sibling event |
| `IQueueService.GetQueue()` | `IMangaQueueService.GetMangaQueue()` | Method rename |

### Phase 8 collapse (TV queue already gone — only cosmetic steps remain)

The TV `Queue` family (`Queue.cs` / `QueueService.cs` / `IQueueService` / `QueueUpdatedEvent` / the Obsolete pair) was **already deleted in the Phase 15 fork**, so the original "collapse the two parallel queues" plan reduces to cosmetic renames:

1. Drop the now-redundant `Protocol == DownloadProtocol.Http` heritage guard from `MangaQueueService.Handle` (the upstream event only carries manga-protocol entries — there is no longer a TV projection to disambiguate from).
2. Rename `MangaQueueService` / `MangaQueueItem` / `MangaQueueUpdatedEvent` / `IMangaQueueService` to canonical positions (drop the `Manga` prefix) — no TV types to delete alongside (already gone).
3. Drop the (now unused) `RemoteEpisode` slot on `TrackedDownload`; rename `RemoteChapter` to the canonical position.

## Cross-References

- [Queue/](../CLAUDE.md) — parent Queue dir (TV Queue family deleted in Phase 15; shared helpers + enums remain)
- [DecisionEngine/Manga/Specifications/QueueDuplicateSpecification.cs](../../DecisionEngine/Manga/Specifications/QueueDuplicateSpecification.cs) — D-20 STUB consumer
- [Download/TrackedDownloads/TrackedDownload.cs](../../Download/TrackedDownloads/TrackedDownload.cs) — RemoteChapter slot
- [Download/Clients/Gateway/](../../Download/Clients/Gateway/CLAUDE.md) — the sole download client; the Phase-36 monitoring loop populates `TrackedDownload.RemoteChapter` (the Phase-4 `Download/Clients/InProcess/` client was deleted in Phase 39)
- [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md) — D-20 decision; Q-3 RESEARCH lock
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — `MangaQueueService` + `QueueDuplicateSpecification` STUB body replacement entries
