# Download/Manga

## Purpose

Manga-side download-monitoring orchestration + the auto-retry loop that closes the *arr
self-healing pipeline.

> **Phase 39 (Plan 39-01) — the Phase-4/6 completion-poller hybrid was RETIRED.** The
> `ProcessMangaCompletedCommand` + `ProcessMangaCompletedDownloads` pair (the archived-CBZ →
> import bridge for the in-process downloader) and `MediaFiles/ChapterArchiving/ChapterArchivedEvent`
> were **deleted** with the in-process vertical. The surviving orchestration keys off the Phase-36
> gateway monitoring loop + the external `GatewayDownloadClient` (Phase 38), NOT an in-process
> completion poll. The retired `ProcessMangaCompletedCommand` `TaskManager.defaultTasks` 1-min row
> was stripped.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\Manga`

## Key Files (verified at HEAD)

| File | Purpose |
|------|---------|
| `MangaCompletedDownloadService.cs` | `OutputPath`-driven import of the finished CBZ the gateway staged (Phase 36 monitoring loop). |
| `MangaDownloadProcessingService.cs` | The gateway-download processing service — `RemoveFailedDownloads()` (ported `DownloadEventHub` failed-removal) calls `MangaDownloadMonitoringService.StopTracking` so a terminally-Failed download leaves the queue before the auto-retry re-search. |
| `RefreshMonitoredMangaDownloadsCommand.cs` / `ProcessMonitoredMangaDownloadsCommand.cs` | The two scheduled commands driving the Phase-36 monitoring loop (registered in `TaskManager.defaultTasks`). |
| `MangaFailedDownloadService.cs` | Publishes `ChapterDownloadFailedEvent` on terminal failure (blocklist/history consumers). |
| `ChapterDownloadCompletedEvent.cs` | Completion event for downstream consumers. |
| `AutoRetryOrchestrator.cs` | Auto-retry loop (Sonarr-parity, **no retry budget** since 2026-06-07). See below. |

## AutoRetryOrchestrator

### Subscribes to `MangaBlocklistAddedEvent` — NOT `ChapterDownloadFailedEvent` (Plan 06-04 ↔ 06-08 anti-race contract)

```csharp
public class AutoRetryOrchestrator : IHandle<MangaBlocklistAddedEvent>  // ← post-Insert event
{
    public void Handle(MangaBlocklistAddedEvent message) { /* push ChapterSearchCommand */ }
}
```

**Why not `ChapterDownloadFailedEvent` directly:** `MangaBlocklistService.Handle(ChapterDownloadFailedEvent)`
(Plan 06-04) and a hypothetical `AutoRetryOrchestrator.Handle(ChapterDownloadFailedEvent)` would both
subscribe to the SAME event. `IEventAggregator.PublishEvent` fans handlers out synchronously, but
handler order on a single event is non-deterministic (DryIoc registration order is enumeration-dependent).

The race: if auto-retry runs BEFORE the blocklist insert, the `ChapterSearchCommand` is queued against
a repo state that does not yet contain the just-failed release's blocklist row. `BlocklistSpecification`
returns Accept, the same release gets re-grabbed → fails → blocklisted → an infinite same-release loop
(there is no retry budget to break it). The fix: subscribe to `MangaBlocklistAddedEvent`. Plan 06-04's
`MangaBlocklistService` enforces the ORDERING INVARIANT — `_repository.Insert(blocklist)` FIRST, then
`PublishEvent(new MangaBlocklistAddedEvent(...))` — so the row is committed when this handler runs.

The `AutoRetryOrchestratorFixture.Subscribes_to_MangaBlocklistAddedEvent_not_ChapterDownloadFailedEvent_ANTI_RACE_GATE`
test asserts (via reflection) the `MangaBlocklistAddedEvent` IHandle IS implemented AND the
`ChapterDownloadFailedEvent` IHandle is NOT.

### No retry budget — Sonarr parity (2026-06-07; debug `auto-retry-one-release-exhaust`)

```csharp
// On every non-manual MangaBlocklistAddedEvent, gated only by AutoRedownloadFailed:
_commandQueueManager.Push(new ChapterSearchCommand(new List<int> { chapterId }));
```

Re-searches on EVERY failure with no per-chapter counter, mirroring Sonarr's
`RedownloadFailedDownloadService.Handle`. The loop is bounded NATURALLY by the blocklist: each failure
blocklists a DIFFERENT release, so the next-best ranks up; once every candidate is blocklisted the
re-search finds nothing acceptable and stops, and the chapter sits in History as `DownloadFailed` for
manual retry (HISTORY-03).

**Why the earlier D-13 `MaxAutoRetriesPerChapter` budget was removed:** it keyed exhaustion on the
LIFETIME count of `DownloadFailed` history rows, so a chapter with ≥ N accumulated failures tripped
"exhausted" on its very FIRST new observed failure — suppressing the next-best re-search that is the
whole point of the *arr promise. The config key + the orchestrator's `IChapterHistoryService` dependency
were both deleted. Do NOT reintroduce a per-chapter retry cap; trust the blocklist to bound the loop.

`AutoRetryOrchestrator` stays as-is — no TV peer to collapse with.

## Cross-References

- Plan 06-04 ordering contract: [src/NzbDrone.Core/Blocklisting/Manga/MangaBlocklistAddedEvent.cs](../../Blocklisting/Manga/MangaBlocklistAddedEvent.cs), [src/NzbDrone.Core/Blocklisting/Manga/MangaBlocklistService.cs](../../Blocklisting/Manga/MangaBlocklistService.cs) (Insert FIRST, then PublishEvent)
- Search command: [src/NzbDrone.Core/IndexerSearch/Manga/ChapterSearchCommand.cs](../../IndexerSearch/Manga/ChapterSearchCommand.cs)
- Import pipeline: [src/NzbDrone.Core/MediaFiles/MangaImport/IImportApprovedChapters.cs](../../MediaFiles/MangaImport/IImportApprovedChapters.cs)
- Gateway download client (sole client): [src/NzbDrone.Core/Download/Clients/Gateway/GatewayDownloadClient.cs](../Clients/Gateway/GatewayDownloadClient.cs)
- Monitoring registry: [src/NzbDrone.Core/Download/TrackedDownloads/MangaDownloadMonitoringService.cs](../TrackedDownloads/MangaDownloadMonitoringService.cs) (`StopTracking`)
- TaskManager registration: [src/NzbDrone.Core/Jobs/TaskManager.cs](../../Jobs/TaskManager.cs)
- Tests: [src/NzbDrone.Core.Test/Download/Manga/](../../../NzbDrone.Core.Test/Download/Manga/) — `AutoRetryOrchestratorFixture`
- Anti-pattern C skill: [.claude/skills/sonarr-consistency-audit/SKILL.md](../../../../.claude/skills/sonarr-consistency-audit/SKILL.md)
