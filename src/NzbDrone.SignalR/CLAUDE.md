# NzbDrone.SignalR

## Purpose

Real-time communication layer using SignalR. Pushes updates to the frontend for live UI updates.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.SignalR\`

## What SignalR Does

- Pushes command progress to UI
- Notifies of download status changes
- Updates series/episode state in real-time
- Broadcasts system messages

## Key Components

| File | Purpose |
|------|---------|
| `SonarrHub.cs` | Main SignalR hub |
| `SignalRBroadcaster.cs` | Event broadcaster |

## Message Types

```csharp
// Command progress
hub.Clients.All.SendAsync("command", commandStatus);

// Series updated
hub.Clients.All.SendAsync("series", seriesResource);

// Episode file added
hub.Clients.All.SendAsync("episodefile", episodeFileResource);

// Queue updated
hub.Clients.All.SendAsync("queue", queueResource);
```

## Frontend Connection

```typescript
// Frontend connects via SignalR client
const connection = new HubConnectionBuilder()
  .withUrl('/signalr/sonarr')
  .build();

connection.on('series', (data) => {
  // Handle real-time series update
});
```

## Events Broadcast

| Event | Triggered By |
|-------|--------------|
| `series` | Series added/updated/deleted |
| `episode` | Episode status change |
| `episodefile` | File imported/deleted |
| `command` | Command started/completed |
| `queue` | Download queue change |
| `health` | Health check change |
| `system` | System status change |

## Cross-References

- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) - Events originate here
- [frontend/CLAUDE.md](../../frontend/CLAUDE.md) - Frontend receives these
