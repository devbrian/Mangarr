# NzbDrone.Core/Download

## Purpose

Download client integration + the **download lifecycle** — from release submission through completion detection to handoff to import. Like indexers, download clients are **ThingiProvider** plugins.

The architecture is largely **media-agnostic**: a torrent client doesn't care if the payload is a `.mkv` or a `.cbz` archive. Most of this directory transfers to Mangarr unchanged.

> **Phase 39 (Plans 39-01/02) — in-process download vertical RETIRED.** The whole
> `Clients/InProcess/` directory (`InProcessImageDownloadClient` + `ChapterDownloadService`
> + `ChapterPageFetcher` + `ChapterDownloadState` + `ChapterDownloadHousekeeper`/
> `HousekeepInProcessDownloadsCommand` + the `Download/Manga/ProcessMangaCompleted*`
> completion-poller cluster) was deleted. **`Clients/Gateway/GatewayDownloadClient.cs`
> (Phase 38) is now the sole download client** — it submits an opaque handle back to the
> external manga gateway and imports the finished CBZ the gateway delivers; Mangarr ships
> zero embedded browser. `DownloadClientFactory`'s fresh-DB auto-seed override was deleted
> too (Sonarr-canonical empty download-client list). The two retired `TaskManager.defaultTasks`
> rows (`ProcessMangaCompletedCommand` 1-min poll + `HousekeepInProcessDownloadsCommand`
> 24-h sweep) were stripped. See `DIVERGENCE.md` Phase 39 section.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\`

## Top-Level Files (verified at HEAD)

| File | Purpose |
|------|---------|
| `IDownloadClient.cs` / `DownloadClientBase.cs` | Provider interface + base |
| `DownloadClientFactory.cs` / `DownloadClientRepository.cs` | ThingiProvider factory + persistence (fresh-DB auto-seed override deleted in Phase 39 — Sonarr-canonical empty list) |
| `DownloadClientProvider.cs` | Picks which configured client to use for a release |
| `DownloadClientItem.cs` / `DownloadClientInfo.cs` / `DownloadClientDefinition.cs` / `DownloadClientType.cs` / `DownloadItemStatus.cs` | In-flight item DTO + status enums + persisted definition |
| `DownloadClientStatus.cs` / `DownloadClientStatusRepository.cs` / `DownloadClientStatusService.cs` | Per-client health/escalation state |
| `IMangaDownloadService.cs` / `MangaDownloadService.cs` | Main entry — submit a `RemoteChapter` to the download client + emit `ChapterGrabbedEvent` (the manga peer of Sonarr's `DownloadService`) |
| `ProvideImportItemService.cs` | Resolve the import item for a tracked download |
| `NzbValidationService.cs` / `InvalidNzbException.cs` | Reference-preserved NZB validation (heritage; no live manga consumer) |
| `ProcessedDecisionResult.cs` / `DownloadsProcessedEvent.cs` / `DownloadCanBeRemovedEvent.cs` | Pipeline result + lifecycle events |
| `DownloadProtocol.cs` (under `Indexers/`) | enum `Unknown = 0` / `Http = 3` — Phase 15 D-18 deleted `Usenet = 1` + `Torrent = 2` (TV download clients removed per D-14; the gap is left rather than renumbered for persisted-int compatibility). `GatewayDownloadClient.Protocol` is `DownloadProtocol.Http` (Phase 1 D-04) |

## Subdirectories

### `Clients/` (verified at HEAD)
The Sonarr Usenet/Torrent client directories (qBittorrent, Transmission, Sabnzbd, NzbGet,
Blackhole, etc.) were **deleted** (never carried into the manga baseline; protocol enum values
removed Phase 15 D-18). What survives:

| Entry | Purpose |
|-------|---------|
| `Gateway/` | `GatewayDownloadClient` — the **sole** download client (Phase 38; external gateway). See [Clients/Gateway/CLAUDE.md](./Clients/Gateway/CLAUDE.md). |
| `DownloadClientSettingsBase.cs` | Shared settings base (memberwise equality) |
| `DownloadClientException.cs` / `DownloadClientAuthenticationException.cs` / `DownloadClientUnavailableException.cs` | Shared client exceptions |

(No `Clients/InProcess/` — the in-process image downloader was retired in Phase 39 Plans 39-01/02.)

### `History/`
`History/Manga/` — per-download grab history (manga sibling; the TV `History/` body was deleted).

### `Extensions/`
`XmlExtensions.cs` — shared XML helper.

### `TrackedDownloads/` (verified at HEAD — manga-only; TV `TrackedDownloadService`/`TrackedDownloadAlreadyImportedService` deleted)
| File | Purpose |
|------|---------|
| `TrackedDownload.cs` | Snapshot DTO (carries the `RemoteChapter` slot) |
| `TrackedDownloadRefreshedEvent.cs` / `TrackedDownloadsRemovedEvent.cs` / `TrackedDownloadStatusMessage.cs` | Registry-refresh events + status message |
| `IMangaDownloadPageProgressSource.cs` | Page-progress lookup the matcher joins on |
| `MangaTrackedDownloadService.cs` | Stateless per-item matcher (`TrackDownload` builds a `TrackedDownload` from the DownloadId history join + page-progress lookup) — Phase 36 manga sibling |
| `MangaDownloadMonitoringService.cs` | The poll heart + registry owner (`List<TrackedDownload>` merged across polls per #301). `IExecute<RefreshMonitoredMangaDownloadsCommand>` + `IHandle<ChapterGrabbedEvent/ChapterImportedEvent>` (5s debounce) + **`IHandle<MangaAddedEvent/MangaUpdatedEvent/MangaBulkEditedEvent/MangaDeletedEvent>` — the issue #278 edit-family cache reconcile** (mirror of Sonarr `TrackedDownloadService` Series-edit handlers; swaps the affected rows' `RemoteChapter.Manga` snapshot in place and republishes `TrackedDownloadRefreshedEvent`, immediate where poll-rebuild was eventual). The single-item trigger is `MangaUpdatedEvent` (not `MangaEditedEvent`) so the `MoveMangaService.RevertPath` rollback + `MangaLinksController` + refresh-pulse paths are covered (CodeRabbit PR #317). **`StopTracking(downloadId)` (debug `auto-retry-one-release-exhaust`, 2026-06-08) — Sonarr `ITrackedDownloadService.StopTracking` peer: evicts a row from the registry + republishes `TrackedDownloadRefreshedEvent` so the queue projection drops it synchronously. Called by `MangaDownloadProcessingService.RemoveFailedDownloads()` (the ported `DownloadEventHub` failed-removal), so a terminally-Failed download leaves the queue before the auto-retry re-search runs — otherwise `QueueDuplicateSpecification` rejects every replacement `chapterAlreadyQueued`.** Phase 36 manga sibling. |

### `Pending/`
Releases awaiting delay-profile timeout / cooldown. `Pending/Manga/` holds the manga sibling
(`MangaPendingReleaseService` + repo + entity — see [Pending/Manga/CLAUDE.md](./Pending/Manga/CLAUDE.md));
`PendingReleaseReason.cs` + `PendingReleasesUpdatedEvent.cs` are shared at the `Pending/` root. The TV
`PendingReleaseService` was deleted with `Tv/`.

### `Manga/` (Phase 6 sibling — gateway-monitoring survivors)
Manga-side download-monitoring orchestration + auto-retry orchestrator. See [Manga/CLAUDE.md](./Manga/CLAUDE.md).

> **Phase 39 (Plan 39-01) — completion-poller cluster retired.** The `ProcessMangaCompletedCommand.cs`
> + `ProcessMangaCompletedDownloads.cs` hybrid (the Phase-4 archived-CBZ → Phase-6 import bridge for the
> in-process downloader) was deleted with the in-process vertical, along with `MediaFiles/ChapterArchiving/ChapterArchivedEvent.cs`.
> The surviving manga-download orchestration now keys off the Phase-36 monitoring loop + the external
> `GatewayDownloadClient` (Phase 38), not an in-process completion poll.

| File | Purpose | Survivor since |
|------|---------|----------------|
| `MangaCompletedDownloadService.cs` | `OutputPath`-driven import of finished CBZs the gateway staged (Phase 36 monitoring loop). | Phase 36 |
| `MangaDownloadProcessingService.cs` / `RefreshMonitoredMangaDownloadsCommand.cs` / `ProcessMonitoredMangaDownloadsCommand.cs` | The gateway-download monitoring loop (replaces the retired in-process completion poller). | Phase 36 |
| `MangaFailedDownloadService.cs` | Publishes `ChapterDownloadFailedEvent` on terminal failure (blocklist/history consumers). | Phase 6 |
| `AutoRetryOrchestrator.cs` | Auto-retry orchestrator (D-12; Sonarr-parity, **no retry budget** since 2026-06-07). Subscribes to `MangaBlocklistAddedEvent` (NOT `ChapterDownloadFailedEvent` — anti-race contract with Plan 06-04 `MangaBlocklistService`); pushes `ChapterSearchCommand` on every non-manual failure (gated by `IConfigService.AutoRedownloadFailed`), mirroring Sonarr's `RedownloadFailedDownloadService` — the blocklist bounds the loop, not a per-chapter cap. | Phase 6 |

The `AutoRetryOrchestrator` stays as-is — no TV peer to collapse with; the auto-retry-redirect-to-next-best pattern is the *arr promise applied to the manga pipeline.

## Lifecycle (gateway-era manga flow)

Post-Phase-39, the sole manga download client is the external `GatewayDownloadClient` — it submits
an opaque handle to the gateway and the gateway delivers a finished CBZ that Mangarr imports. The
manga flow uses the `RemoteChapter` / `MangaImport` lifecycle (NOT the TV `RemoteEpisode` /
`EpisodeImport` lifecycle, which is reference-preserved fork heritage):

```
MangaDownloadDecisionMaker → Approved DownloadDecision
        ↓
DownloadService.DownloadReport(decision)   // submits a RemoteChapter
        ↓
DownloadClientProvider.GetDownloadClient(...)  → GatewayDownloadClient (sole manga client)
        ↓
IDownloadClient.Download(remoteChapter, indexer)
        ↓ (submit opaque handle to the external gateway; the gateway owns the browser + fetch)
        ↓
ChapterGrabbedEvent published, ChapterHistory entry written, SignalR push
        ↓
Phase-36 monitoring loop (RefreshMonitoredMangaDownloadsCommand / ProcessMonitoredMangaDownloadsCommand)
        ↓
MangaCompletedDownloadService.Process()  // OutputPath-driven, NOT an in-process completion poll
        ├─ gateway reports Completed?
        ├─ staged CBZ reachable at OutputPath?
        └─ ImportApprovedChapters (in NzbDrone.Core/MediaFiles/MangaImport/)
        ↓
ChapterImportedEvent (Pitfall-4 LAST line) / DownloadCompletedEvent
```

The TV `EpisodeGrabbedEvent` → `ImportApprovedEpisodes` → `EpisodeImportedEvent` chain still exists
for the reference-preserved Usenet/Torrent clients but is not part of the manga gateway flow.

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

## Manga Adaptation Notes (gateway-era end state)

The manga download story is **settled**: `GatewayDownloadClient` (Phase 38, `Clients/Gateway/`) is
the **sole manga download client**. Mangarr submits an opaque handle to the external manga gateway,
which owns the embedded browser + anti-bot clearance + per-page image fetch; the gateway delivers a
finished CBZ that Mangarr imports. **Mangarr ships zero embedded browser.**

**Do NOT add an in-process scraper download client.** The Phase-4/6 in-process image-scraper vertical
(`Clients/InProcess/`: `InProcessImageDownloadClient` + page-fetcher + archiver + `ChapterDownloadState`)
was **retired in Phase 39 (Plans 39-01/02)** — its premise (Mangarr running the browser/fetch loop
itself) is exactly what the gateway architecture replaced. Any "image-scraper download client" / `Clients/MangaScraper/`
proposal is a regression of that retirement.

- Direct HTTP image scraping → owned by the **external gateway**, not an in-process client.
- Direct CBZ/CBR download → the gateway delivers the finished CBZ at its `OutputPath`; the
  Phase-36 monitoring loop imports it (no "Folder-watch" client needed for the manga flow).
- Torrents (manga packs on Nyaa, etc.) → the reference-preserved Usenet/Torrent clients still work
  as fork heritage, but are NOT a first-class manga path.

### `DownloadProtocol` — no extension needed
`DownloadProtocol` is `{ Unknown = 0, Http = 3 }` (Phase 15 D-18 deleted `Usenet = 1` + `Torrent = 2`).
The manga gateway client uses `DownloadProtocol.Http` (Phase 1 D-04) — no new `Direct`/`Scraper`
value is required, and none should be added for an in-process flow that no longer exists.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Produces approved decisions
- [../MediaFiles/CLAUDE.md](../MediaFiles/CLAUDE.md) — Imports completed downloads
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — Source of releases
- [../Queue/](../Queue/) — Live in-flight queue
- [../History/](../History/) — Persistent grab/import history
