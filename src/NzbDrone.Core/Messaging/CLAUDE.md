# NzbDrone.Core/Messaging

## Purpose

The **event + command** infrastructure. Two related but distinct mechanisms:

- **Events** — fire-and-forget broadcasts. Multiple subscribers (`IHandle<TEvent>`) react asynchronously. Used for "something happened" (SeriesAdded, EpisodeImported, etc.).
- **Commands** — user-initiated actions tracked through a queue with progress + status. One executor per command type (`IExecute<TCommand>`).

This is **infrastructure** and reusable as-is for Mangarr.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Messaging\`

## Subdirectories

### `Events/`

| File | Purpose |
|------|---------|
| `IEventAggregator.cs` | The publish-only interface. Single method: `void PublishEvent<TEvent>(TEvent @event) where TEvent : class, IEvent;` |
| `EventAggregator.cs` | Implementation. Resolves all `IHandle<TEvent>` instances and dispatches. |
| `IHandle.cs` | Subscriber contract: `void Handle(TEvent message);` |
| `IHandleAsync.cs` | Async fan-out variant |
| `IEvent.cs` | Marker interface; all events implement it |
| `ApplicationStartedEvent.cs`, `ApplicationShutdownRequestedEvent.cs` | Lifecycle events |
| `ModelEvent<TModel>.cs` | Generic event published by `BasicRepository` on insert/update/delete |

### `Commands/`

| File | Purpose |
|------|---------|
| `Command.cs` | Base class. `Name`, `LastExecutionTime`, `Status`, `Progress`, etc. |
| `ICommandQueueManager.cs` / `CommandQueueManager.cs` | Queue + persistence |
| `IExecute.cs` | Executor contract: `void Execute(TCommand message);` |
| `CommandFactory.cs` | Build commands from JSON (REST → controller → factory) |
| `CommandResult.cs`, `CommandStatus.cs`, `CommandPriority.cs`, `CommandTrigger.cs` | DTOs |
| `Tracking/CommandQueue.cs` | In-memory queue, persistence layer for restartability |
| `Events/CommandExecutedEvent.cs` | When command finishes |
| `MessageAggregator.cs` (vestigial) | Older bus |

## Event Pattern

```csharp
// 1. Define an event
public class SeriesAddedEvent : IEvent
{
    public Series Series { get; }
    public bool DoRefresh { get; }
    public SeriesAddedEvent(Series series, bool doRefresh) { Series = series; DoRefresh = doRefresh; }
}

// 2. Publish (anywhere)
_eventAggregator.PublishEvent(new SeriesAddedEvent(series, true));

// 3. Subscribe (DI auto-discovers)
public class SeriesAddedHandler : IHandle<SeriesAddedEvent>
{
    public void Handle(SeriesAddedEvent message)
    {
        // React: trigger refresh, scan, notification, etc.
    }
}
```

Handlers are auto-registered via convention (see `NzbDrone.Common/Composition/`). Multiple handlers per event are OK — they all run.

## Command Pattern

```csharp
// 1. Define a command (POCO inheriting Command base)
public class RefreshSeriesCommand : Command
{
    public List<int> SeriesIds { get; set; }
    public override string CompletionMessage => "Series refresh completed";
}

// 2. Define an executor (DI auto-discovers — single-handler dispatch via IExecute<T>)
public class RefreshSeriesCommandExecutor : IExecute<RefreshSeriesCommand>
{
    public void Execute(RefreshSeriesCommand message) { /* side-effect work */ }
}

// 3. Queue it via API: POST /api/v5/command { "name": "RefreshSeriesCommand", "seriesIds": [1,2,3] }
//    Or programmatically:
_commandQueueManager.Push(new RefreshSeriesCommand { SeriesIds = ids });
```

Commands are persisted (so they survive restart), executed in priority order, and their progress is broadcast to the UI via SignalR.

### Anti-pattern C — `TaskManager.defaultTasks` vs migration seed

Scheduled commands (e.g. `RefreshMangaCommand` 12h cadence, `MangaRssSyncCommand`, `MissingChapterSearchCommand`, `ProcessMangaCompletedCommand`) MUST be registered in `Jobs/TaskManager.defaultTasks` at runtime, NOT seeded via Migration 001 `Insert.IntoTable("ScheduledTasks")`. The structural fixture [`TaskManagerDefaultTasksFixture`](../../NzbDrone.Core.Test/JobTests/TaskManagerDefaultTasksFixture.cs) enforces both halves: (a) per-command `_taskManagerSource.Should().Contain("typeof(NewMangaCommand).FullName", ...)` assertions; (b) `Migration_001_contains_zero_Insert_IntoTable_calls` floor.

The bug class was first surfaced in Phase 6 — Plan 06-06 wired `MangaRssSyncCommand` + `MissingChapterSearchCommand` runtime registrations after they had been silently shipped without cadence; Plan 06-08 wired `ProcessMangaCompletedCommand`. The `sonarr-consistency-audit` skill ([.claude/skills/sonarr-consistency-audit/SKILL.md](../../../.claude/skills/sonarr-consistency-audit/SKILL.md)) was authored to catch this exact pattern in future phases. Phase 11 swept the full inventory (16 registrations as of 2026-05-06) and verified zero gaps. See [Jobs/CLAUDE.md](../Jobs/CLAUDE.md) for the registration template + sister-fixture-row mandate.

### Anti-pattern D — `UnknownCommandExecutor` silent fallback

`CommandExecutor.ExecuteCommand` resolves the handler via `_serviceFactory.Build(typeof(IExecute<TCommand>))`. If NO concrete `IExecute<TCommand>` is registered for a queued command, DryIoc auto-discovery falls through to [`UnknownCommandExecutor`](Commands/UnknownCommandExecutor.cs) — silently. The command body is logged at Debug, the substrate publishes `CommandExecutedEvent`, and the user-facing UI sees a "completed" command that did nothing.

**Rule:** every new `*Command.cs` MUST ship its `IExecute<>` implementer in the same plan. Phase 11 Plan 11-06 closed the canonical instance — `DeleteMangaFilesCommand` had been silently swallowed since Phase 8 cluster-02 (class shipped without handler). Plan 11-06 added `IExecute<DeleteMangaFilesCommand>` to `ChapterFileService` (manga peer of `MediaFileDeletionService.IExecute<DeleteSeriesFilesCommand>`); the silent `UnknownCommandExecutor` fallback for manga bulk-delete is now closed. See [`MediaFiles/CLAUDE.md`](../MediaFiles/CLAUDE.md) for the handler attribution row.

## Common Events (Observed in Code)

| Event | Triggered By |
|-------|--------------|
| `SeriesAddedEvent` | Series added |
| `SeriesUpdatedEvent` | Series edited |
| `SeriesDeletedEvent` | Series removed |
| `SeriesRefreshStartingEvent` | Before metadata fetch |
| `SeriesScannedEvent` | After disk scan |
| `EpisodeFileAddedEvent` | New file linked |
| `EpisodeFileDeletedEvent` | File removed |
| `EpisodeImportedEvent` | Successful import |
| `EpisodeGrabbedEvent` | Release sent to download client |
| `DownloadCompletedEvent` | Download finished |
| `DownloadFailedEvent` | Download failed |
| `RssSyncCompleteEvent` | After RSS run |
| `HealthCheckCompletedEvent` | After health check run |
| `ApplicationStartedEvent` / `ApplicationShutdownRequestedEvent` | Lifecycle |

## Common Commands

(See [Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) for the user-callable list.)

`RefreshSeries`, `RescanSeries`, `EpisodeSearch`, `SeasonSearch`, `SeriesSearch`, `RssSync`, `RenameFiles`, `Backup`, `ApplicationUpdate`, `Housekeeping`, `MessagingCleanup`, `CheckHealth`, etc.

## Manga Adaptation Notes

This is **infrastructure** — no architectural changes needed. The events that mention "Series" / "Episode" will be renamed in lockstep with the Tv/ migration:
- `SeriesAddedEvent` → `MangaAddedEvent`
- `EpisodeImportedEvent` → `ChapterImportedEvent`
- etc.

All subscribers (`IHandle<X>`) update accordingly.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Tv/Events/](../Tv/Events/) — Series-specific events
- [../MediaFiles/Events/](../MediaFiles/Events/) — File-specific events
- [../../NzbDrone.SignalR/CLAUDE.md](../../NzbDrone.SignalR/CLAUDE.md) — Translates these events to SignalR pushes
- [../../Mangarr.Api.V5/Commands/](../../Mangarr.Api.V5/Commands/) — REST endpoint for commands
