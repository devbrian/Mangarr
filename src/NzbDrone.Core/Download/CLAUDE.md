# NzbDrone.Core/Download

## Purpose

Download client integration + the **download lifecycle** — from release submission through completion detection to handoff to import. Like indexers, download clients are **ThingiProvider** plugins.

The architecture is largely **media-agnostic**: a torrent client doesn't care if the payload is a `.mkv` or a `.cbz` archive. Most of this directory transfers to Mangarr unchanged.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `IDownloadClient.cs` / `DownloadClientBase.cs` | Provider interface + base |
| `DownloadClientItem.cs` | Status of a single in-flight download |
| `IDownloadClientFactory.cs` / `DownloadClientFactory.cs` | ThingiProvider factory |
| `DownloadClientProvider.cs` | Picks which configured client to use for a release |
| `DownloadService.cs` | Main entry — submit a `RemoteEpisode` to a download client |
| `CompletedDownloadService.cs` | Detect completed downloads, hand to importer |
| `FailedDownloadService.cs` | Detect failed downloads, blocklist + notify |
| `IgnoredDownloadService.cs` | Skip (ignore) downloads matching certain criteria |
| `DownloadEventHub.cs` | Translates download client events into domain events |
| `RedownloadFailedDownloadService.cs` | Auto-retry failed grabs |
| `UsenetClientItem.cs` / `TorrentClientItem.cs` | Specialized item types |
| `DownloadProtocol.cs` | enum `Usenet` / `Torrent` |
| `DownloadFailedReason.cs` | enum |

## Subdirectories

### `Clients/`
Concrete download client integrations. Each typically has:
- `<Client>.cs` — provider class
- `<Client>Settings.cs` — settings + validator
- Optionally request/response DTOs

| Client | Protocol | Type |
|--------|----------|------|
| `qBittorrent/` | Torrent | API-based |
| `Transmission/` | Torrent | RPC |
| `Deluge/` | Torrent | JSON-RPC |
| `rTorrent/` | Torrent | XML-RPC |
| `Vuze/` | Torrent | (similar to Transmission) |
| `Flood/` | Torrent | API |
| `Hadouken/` | Torrent | API |
| `UTorrent/` | Torrent | API |
| `Aria2/` | Both | XML-RPC |
| `Sabnzbd/` | Usenet | API |
| `NzbGet/` | Usenet | API |
| `DownloadStation/` | Both | Synology |
| `FreeboxDownload/` | Both | Freebox |
| `Pneumatic/` | Usenet | Folder watch |
| `UsenetBlackhole/` | Usenet | Folder watch |
| `TorrentBlackhole/` | Torrent | Folder watch |

### `History/`
Per-download grab history (separate from generic episode history). Tracks the indexer + outcome of each grab attempt.

### `TrackedDownloads/`
| File | Purpose |
|------|---------|
| `TrackedDownloadService.cs` | In-memory cache of active downloads polled from clients |
| `TrackedDownload.cs` | Snapshot DTO |
| `TrackedDownloadAlreadyImportedService.cs` | Detect already-imported items |

### `Aggregation/`
Aggregates parsed-from-client info with parsed-from-title info (e.g., resolves `RemoteEpisode` from a download client item).

### `Pending/`
Releases awaiting delay-profile timeout / preferred-protocol cooldown:
| File | Purpose |
|------|---------|
| `PendingReleaseService.cs` | Add / process pending |
| `PendingRelease.cs` | DB entity |
| `PendingReleasesController.cs` (under `Sonarr.Api.V5`) | UI access |

### `Extensions/`
Shared utilities (`MagnetLink.cs`, `TorrentBitfield.cs`, etc.).

### `Manga/` (Phase 6 sibling)
Manga-side staging-handoff orchestration + auto-retry orchestrator. See [Manga/CLAUDE.md](./Manga/CLAUDE.md).

| File | Purpose | Phase 6 Plan |
|------|---------|--------------|
| `ProcessMangaCompletedCommand.cs` | Payload-less `Command` POCO; 1-min poll trigger registered in `TaskManager.defaultTasks` per Anti-pattern C compliance. | Plan 06-08 |
| `ProcessMangaCompletedDownloads.cs` | Hybrid `IHandle<ChapterArchivedEvent>` + `IExecute<ProcessMangaCompletedCommand>` orchestrator (RESEARCH Pattern 1). Bridges Phase 4 archived-CBZ output to Phase 6 `ImportApprovedChapters` (Plan 06-07). Idempotent — both paths converge on a single `ProcessOne` method that short-circuits when `ChapterFile` is already present. | Plan 06-08 |
| `AutoRetryOrchestrator.cs` | Bounded auto-retry orchestrator (D-12 + D-13 + Pitfall 5). Subscribes to `MangaBlocklistAddedEvent` (NOT `ChapterDownloadFailedEvent` — anti-race contract with Plan 06-04 `MangaBlocklistService`); pushes `ChapterSearchCommand` while `ChapterHistory{DownloadFailed}` count < `IConfigService.MaxAutoRetriesPerChapter`. | Plan 06-08 |

**Phase 8 cleanup:** the staging-handoff path collapses with `CompletedDownloadService` when `ImportApprovedEpisodes` deletes; the TV-side `Protocol == DownloadProtocol.Http` early-return guard at lines 74-77 disappears with it. The `AutoRetryOrchestrator` stays as-is — no TV peer to collapse with; the auto-retry-redirect-to-next-best pattern is the *arr promise applied to the manga pipeline.

## Lifecycle

```
DownloadDecisionMaker → Approved DownloadDecision
        ↓
DownloadService.DownloadReport(decision)
        ↓
DownloadClientProvider.GetDownloadClient(protocol, indexerId)
        ↓
IDownloadClient.Download(remoteEpisode, indexer)
        ↓ (HTTP call to qBittorrent/SAB/etc.)
        ↓
EpisodeGrabbedEvent published, History entry written, SignalR push
        ↓
TrackedDownloadService polls clients periodically (CheckForFinishedDownloadCommand)
        ↓
CompletedDownloadService.Process()
        ├─ status == Completed?
        ├─ Path/files reachable?
        └─ ImportApprovedEpisodes (in NzbDrone.Core/MediaFiles/EpisodeImport/)
        ↓
EpisodeImportedEvent / DownloadCompletedEvent
```

## Adding a New Download Client

1. Create folder `Clients/MyClient/`.
2. `MyClient.cs` extends `DownloadClientBase<MyClientSettings>`.
3. Implement:
   - `protected override string AddFromMagnetLink(...)`, `AddFromTorrentFile(...)`, or `AddFromNzbFile(...)`
   - `public override IEnumerable<DownloadClientItem> GetItems()`
   - `public override DownloadClientInfo GetStatus()`
   - `public override void RemoveItem(DownloadClientItem item, bool deleteData)`
   - `public override DownloadClientStatus Test()`
4. `MyClientSettings.cs` implements `IDownloadClientSettings` (host, port, user, password, category, etc.) with its validator.
5. Auto-discovered. Add tests under `NzbDrone.Core.Test/Download/DownloadClientTests/MyClientFixture.cs`.

## DownloadClientProvider Selection Logic

When multiple clients of the same protocol are configured:
1. Filter to only enabled clients matching the protocol
2. Filter to those allowing the indexer's tags (if tag-based routing)
3. Prefer the highest-priority client (lower number = higher priority)
4. Round-robin within same priority

## Manga Adaptation Notes

This entire directory is **largely reusable** as-is. Manga downloads are typically:

- Direct HTTP image scraping (per-page fetches assembled into CBZ) — this is **not** a torrent/usenet flow. A new download client type may be needed: an in-process "image-scraper download client" that pulls pages from the source site and packages them.
- Torrents (manga packs on Nyaa, etc.) — works as-is with qBittorrent etc.
- Direct CBZ/CBR download via HTTP — could use a "Folder-watch" style client.

### Recommended New Clients
1. `Clients/MangaScraper/` — In-process scraper that fetches images, packages into CBZ, places in a watched completion folder.
2. `Clients/HttpDirect/` — Generic HTTP download for direct CBZ/CBR links.

The existing torrent clients work without changes for users who get manga via torrents.

### `DownloadProtocol` Extension
Currently `DownloadProtocol { Usenet, Torrent }`. May need a new value `Direct` or `Scraper` for image-scraper flow:

```csharp
public enum DownloadProtocol { Unknown, Usenet, Torrent, Direct }
```

Adding a new protocol affects:
- `IDownloadClient.Protocol`
- `IIndexer.Protocol`
- DelayProfile per-protocol settings
- UI dropdowns for indexer/client config

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Produces approved decisions
- [../MediaFiles/CLAUDE.md](../MediaFiles/CLAUDE.md) — Imports completed downloads
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — Source of releases
- [../Queue/](../Queue/) — Live in-flight queue
- [../History/](../History/) — Persistent grab/import history
