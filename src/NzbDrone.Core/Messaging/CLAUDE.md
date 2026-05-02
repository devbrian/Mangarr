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
// 1. Define a command
public class RefreshSeriesCommand : Command
{
    public List<int> SeriesIds { get; set; }
    public override string CompletionMessage => "Series refresh completed";
}

// 2. Define an executor
public class RefreshSeriesCommandExecutor : IExecute<RefreshSeriesCommand>
{
    public void Execute(RefreshSeriesCommand message) { /* … */ }
}

// 3. Submit via API: POST /api/v5/command { "name": "RefreshSeriesCommand", "seriesIds": [1,2,3] }
//    Or programmatically:
_commandQueueManager.Push(new RefreshSeriesCommand { SeriesIds = ids });
```

Commands are persisted (so they survive restart), executed in priority order, and their progress is broadcast to the UI via SignalR.

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

(See [Sonarr.Api.V5/CLAUDE.md](../../Sonarr.Api.V5/CLAUDE.md) for the user-callable list.)

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
- [../../Sonarr.Api.V5/Commands/](../../Sonarr.Api.V5/Commands/) — REST endpoint for commands
