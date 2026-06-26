# NzbDrone.SignalR

## Purpose

Real-time push channel from backend to frontend using SignalR. Pushes commands, entity changes, queue updates, health, and system messages so the UI stays live without polling.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.SignalR\`

## Files (only 3)

| File | Purpose |
|------|---------|
| `MessageHub.cs` | The SignalR `Hub` class (named `MessageHub`, **not** `SonarrHub`). Also contains `SignalRMessageBroadcaster` which implements `IBroadcastSignalRMessage`. |
| `IBroadcastSignalRMessage.cs` | Interface used by `NzbDrone.Core` to broadcast without taking a hard dependency on SignalR. |
| `SignalRMessage.cs` | Message envelope DTO sent to clients. |

## Hub Endpoint

The hub is mapped at **`/signalr/messages`** (registered in [NzbDrone.Host/Startup.cs](../NzbDrone.Host/Startup.cs)).

## What MessageHub Does

```csharp
public class MessageHub : Hub
{
    public override Task OnConnectedAsync()
    {
        // Track connection in a static HashSet, send initial "version" message
    }

    public override Task OnDisconnectedAsync(Exception exception)
    {
        // Remove from connection set
    }
}
```

`SignalRMessageBroadcaster`:
- Holds the IHubContext<MessageHub>
- `IsConnected` property (true if any client connected)
- `BroadcastMessage(SignalRMessage message)` → sends to all clients

## Usage from Core

`NzbDrone.Core` does **not** reference `Microsoft.AspNetCore.SignalR`. It depends only on `IBroadcastSignalRMessage`, which is implemented by `SignalRMessageBroadcaster`. Decoupling means Core compiles without ASP.NET dependencies.

`SignalRMessageBroadcaster` listens (via `IHandle<TEvent>`) to relevant events in Core and translates them into SignalR messages.

## Message Types Sent to UI

Resource names are auto-derived from each `*Resource.ResourceName` (lowercase, `resource` suffix stripped). The `SignalRListener.tsx` frontend handler currently dispatches on: `manga`, `chapter`, `chapterfile`, `calendar`, `command`, `connection`, `downloadclient`, `health`, `importlist`, `indexer`, `metadata`, `rootfolder`, `tag`, `version`.

| Message name | Triggered By |
|--------------|--------------|
| `manga` | Manga added / updated / deleted (+ chapter-list / cover / chapter-file changes — see `MangaController` IHandle list) |
| `chapter` | Chapter status change (monitored toggle) |
| `chapterfile` | ChapterFile imported / deleted / renamed |
| `command` | Command queued / started / finished / failed |
| `manga/queue`, `manga/queue/status` | Queue item / counter changes (see `Manga/Queue/CLAUDE.md`) |
| `health` | Health check status change |
| `tag` | Tag created / updated / deleted |
| `rootfolder` | Root folder added / removed |
| `version` | Sent on connect with backend version |

## Message Envelope

```csharp
public class SignalRMessage
{
    public string Name { get; set; }       // e.g. "series", "command"
    public ModelAction Action { get; set; } // Sync | Created | Updated | Deleted
    public object Body { get; set; }       // resource DTO
}
```

## Frontend Connection

Frontend wraps SignalR in a Redux middleware (or React component `<SignalRListener />`). Conceptual usage:

```typescript
const connection = new HubConnectionBuilder()
  .withUrl(`${urlBase}/signalr/messages?access_token=${apiKey}`)
  .build();

connection.on('manga', (msg) => { /* dispatch update */ });
connection.start();
```

See [frontend/src/Components/SignalRListener.tsx](../../frontend/src/Components/SignalRListener.tsx) and [frontend/src/Store/Middleware/](../../frontend/src/Store/Middleware/).

## Manga Adaptation Notes

- This project is media-agnostic infrastructure (the `MessageHub` / `SignalRMessage` envelope is entity-neutral). The entity rename landed in Phase 15: message names are now `manga` / `chapter` / `chapterfile`, auto-derived from each `*Resource.ResourceName` — no `series`/`episode` names remain. Backend broadcasters and frontend `connection.on(...)` handlers stay in lockstep via the auto-derived name.

## Cross-References

- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) — Events that originate the messages
- [NzbDrone.Host/CLAUDE.md](../NzbDrone.Host/CLAUDE.md) — Hub endpoint registration
- [frontend/CLAUDE.md](../../frontend/CLAUDE.md) — Frontend client side
