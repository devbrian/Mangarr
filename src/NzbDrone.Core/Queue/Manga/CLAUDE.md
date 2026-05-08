# Queue/Manga

## Purpose

Phase 6 D-20 manga sibling of `src/NzbDrone.Core/Queue/` (TV `Queue` family). Ships the `MangaQueueService` static-list projection that fans the `IHandle<TrackedDownloadRefreshedEvent>` lifecycle into a manga-shaped `List<MangaQueueItem>` filtered to `DownloadProtocol.Http` entries, and emits `MangaQueueUpdatedEvent` on every refresh. Wires the Phase 5 `QueueDuplicateSpecification` STUB (D-20) so the decision engine can ask "is this chapter already in flight?" instead of accepting every release.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Queue\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaQueueItem.cs` | POCO row. Wire-shape sibling of `Queue/Queue.cs`. Drops TV-only `Languages` / `QualityModel`; adds `TranslatedLanguage` + `ScanlationGroup` as first-class fields (Phase 3 D-Q4 wire shape). Carries `RemoteChapter` so `QueueDuplicateSpecification` can intersect on `Chapter.Id` lists. `Id` is computed via `HashConverter.GetHashInt31` over `"trackedDownload-{client}-{downloadId}-{chapterId}"` so the same projection re-issues the same `Id` across refreshes (SignalR diff key). |
| `MangaQueueUpdatedEvent.cs` | Marker `IEvent`. Emitted by `MangaQueueService.Handle` on every successful projection refresh. Plan 06-09 V5 controller subscribes via SignalR to fan diffs out to the React Activity panel. Also defines `MangaPendingReleasesUpdatedEvent` (sibling of `PendingReleasesUpdatedEvent`) reserved for Plan 06-08 auto-retry orchestration. |
| `IMangaQueueService.cs` | Contract: `GetMangaQueue() : List<MangaQueueItem>`, `Find(int) : MangaQueueItem`, `Remove(int) : void`. Mirrors `IQueueService` verbatim — only the row type is manga-specific. |
| `MangaQueueService.cs` | Service. `IHandle<TrackedDownloadRefreshedEvent>` static-list projection (Q-3 RESEARCH lock — single-writer / process-wide pattern verbatim from TV `QueueService.cs:24`). Filters to `t.IsTrackable && t.Protocol == DownloadProtocol.Http` so the TV and manga queues co-exist on the same upstream event without double-counting. Maps one row per `RemoteChapter.Chapters` entry; emits a single shell row when `RemoteChapter` is null (orphan recovery path). |

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

The static field is the same anti-pattern Mangarr's TV `QueueService` uses verbatim and has been stable through five major versions. T-06-10 (concurrent-write race) is mitigated by the **single-writer** model: only the `IHandle` path mutates `_queue`. Reads (`GetMangaQueue`, `Find`) defensive-copy via `ToList()` so iteration during a refresh doesn't tear.

### Protocol filter — TV / manga co-existence on the same event

`TrackedDownloadRefreshedEvent` carries TV (`Usenet` / `Torrent`) AND manga (`Http`) entries together. Both `QueueService` (TV) and `MangaQueueService` (manga) subscribe — each filters to its own protocol set:

| Service | Filter | Notes |
|---------|--------|-------|
| `QueueService` (TV) | (no protocol filter; populates all rows) | Pre-existing — sees Http rows too. Plan 09 V5 controller verifies the V5 endpoints separate the two so the UI does not double-count. |
| `MangaQueueService` (manga) | `t.Protocol == DownloadProtocol.Http` | New (this plan). Manga-only. |

Phase 8 collapse plan: when `Tv/` deletes, drop `Protocol == Http` filter; only one queue remains.

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

`TrackedDownload` gained an optional `RemoteChapter` slot in this plan parallel to the pre-existing `RemoteEpisode`. Phase 4's `InProcessImageDownloadClient` populates it on the manga-protocol path so the projection has a real row to map. Phase 8 collapse: when `Tv/` deletes, the surviving slot carries the unified DTO.

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

### Phase 8 collapse

When `Tv/` deletes, the parallel queue siblings collapse to a single canonical `Queue` family:

1. Drop `Protocol == DownloadProtocol.Http` filter from `MangaQueueService.Handle` — the upstream event will only carry manga-protocol entries by then.
2. Rename `MangaQueueService` / `MangaQueueItem` / `MangaQueueUpdatedEvent` / `IMangaQueueService` to canonical positions (drop the `Manga` prefix). The TV `Queue` / `QueueService` / `IQueueService` / `QueueUpdatedEvent` go away in the same commit.
3. Drop the `RemoteEpisode` slot on `TrackedDownload`; rename `RemoteChapter` to the canonical position.

## Cross-References

- [Queue/](../CLAUDE.md) — TV Queue family
- [DecisionEngine/Manga/Specifications/QueueDuplicateSpecification.cs](../../DecisionEngine/Manga/Specifications/QueueDuplicateSpecification.cs) — D-20 STUB consumer
- [Download/TrackedDownloads/TrackedDownload.cs](../../Download/TrackedDownloads/TrackedDownload.cs) — RemoteChapter slot
- [Download/Clients/InProcess/](../../Download/Clients/InProcess/CLAUDE.md) — Phase 4 in-process client populates `TrackedDownload.RemoteChapter`
- [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md) — D-20 decision; Q-3 RESEARCH lock
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — `MangaQueueService` + `QueueDuplicateSpecification` STUB body replacement entries
