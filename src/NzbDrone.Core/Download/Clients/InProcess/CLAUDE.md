# Download/Clients/InProcess

## Purpose

Phase 4 in-process `IDownloadClient` — Mangarr's v1 default download client. Downloads chapter images directly within the application process (no external client required) using a bounded `Channel<T>` orchestrator with per-source / per-chapter parallelism. Persistent state lives in the `ChapterDownloadState` table; per-page bytes land in the configurable scratch directory.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\Clients\InProcess`

## Key Files

| File | Purpose |
|------|---------|
| `InProcessImageDownloadClient.cs` | The `IDownloadClient` impl (auto-discovered). Extends `DownloadClientBase<T>` directly per Phase 1 D-04 (manga is neither Usenet nor Torrent). |
| `InProcessImageDownloadClientSettings.cs` | Per-instance Settings: `DownloadsPerSource` (2), `PagesPerChapter` (4), `RetentionDays` (7). |
| `ChapterDownloadService.cs` | Bounded `Channel<T>` orchestrator. Per-source channels (capacity = `DownloadsPerSource`); per-chapter try/catch envelope ensures DOWNLOAD-05 contract. |
| `ChapterDownloadJob.cs` | Per-chapter unit of work consumed off the Channel. |
| `ChapterPageFetcher.cs` | Per-page HTTP GET with `RateLimitKey = SourceKey` (Phase 1 D-11) + honest UA + Polly retry. Pitfall 1 / F-01 mitigation lives here — `RateLimitKey` MUST be set on every GET. |
| `IChapterPageFetcher.cs` | Page-fetch contract. |
| `ChapterDownloadState.cs` | Hybrid resumable-state DB row (own `ModelBase` per Phase 3 D-17 precedent; clean Phase 8 DROP). |
| `ChapterDownloadStatus.cs` | Lifecycle enum: Queued, Downloading, Completing, Completed, Failed. |
| `ChapterDownloadStateRepository.cs` | Repo inheriting Phase 1 D-15 Polly resilience for SQLITE_BUSY. |
| `IChapterDownloadStateRepository.cs` | Repo contract — `AllInFlight`, `ByStatus`, `FindByMangaAndChapter`, `DeleteOrphans`. |
| `IChapterDownloadService.cs` | Orchestrator contract used by `InProcessImageDownloadClient`. |
| `ChapterDownloadHousekeeper.cs` | Daily-scheduled retention sweep + orphan scratch cleanup. Registered via `TaskManager.defaultTasks` (NOT migration seed — Phase 2 audit lesson). |
| `HousekeepInProcessDownloadsCommand.cs` | The `Command` the housekeeper executes. |

## Patterns / Conventions

- **`Protocol = DownloadProtocol.Http` (Phase 1 D-04)** — gates the Phase 4 D-10 early-return in `CompletedDownloadService.Check()` so Mangarr's TV-shaped `ImportApprovedEpisodes` does not auto-fire on a manga CBZ.
- **`Channel<T>` per source**: `BoundedChannelFullMode.Wait`; writer runs on a fire-and-forget Task per source so `Download()` returns immediately (Pitfall 5).
- **Page filenames**: `<PageIndex:D4>.<ext>` — 4-digit zero-padded preserving original ext. Lex sort = page order; reader-compat across Komga / Kavita / Mihon / ComicRack.
- **Scratch dir per row**: `<Config.DownloadScratchPath>/<row.Id>/<NNNN>.<ext>` — namespaced by row Id (NOT chapter Id) so retries-after-failed produce a fresh scratch dir.
- **D-03 manifest re-fetch**: 403/410 → re-fetch manifest ONCE → retry the page once. Second 403/410 = chapter terminal failure.
- **Housekeeper registration via `TaskManager.defaultTasks`** (NOT migration seed) — sonarr-consistency-audit anti-pattern C is enforced via `ChapterDownloadHousekeeperRegistrationFixture` (real migrated SQLite, two-step pre-Init / post-Init assertion; NO source-text grep).

## Manga Adaptation Notes

- **Cross-volume scratch ↔ library** is supported (Pitfall 6): `Config.DownloadScratchPath` may live on SSD while the eventual library lives on NAS. Phase 4's archive write (`<staging>/<chapter>.cbz.tmp` → `<staging>/<chapter>.cbz`) is ALWAYS same-volume by construction. The cross-volume cost surfaces in Phase 6 import (`ImportApprovedChapters` does the staging → library move; cross-volume cases become copy + delete rather than atomic rename).
- **Phase 8 collapse**: `Download(RemoteEpisode, IIndexer)` shim renames to `Download(RemoteChapter, IIndexer)`. The TV-shaped overload is a thin shim until then; manga path peels off via `release.IndexerId` lookup of the `IHttpAggregator` plugin.
- **Mangarr divergence**: extends `DownloadClientBase<TSettings>` directly (NOT `UsenetClientBase` / `TorrentClientBase`). Documented in `DIVERGENCE.md`.

## Cross-References

- [src/NzbDrone.Core/Indexers/Http/IHttpAggregator.cs](../../../Indexers/Http/IHttpAggregator.cs) — non-generic marker the orchestrator uses
- [src/NzbDrone.Core/Indexers/Http/HttpAggregatorBase.cs](../../../Indexers/Http/HttpAggregatorBase.cs) — `GetChapterPages` abstract (Phase 4 D-01)
- [src/NzbDrone.Core/MediaFiles/ChapterArchiving/CLAUDE.md](../../../MediaFiles/ChapterArchiving/CLAUDE.md) — sibling archiver plugin contract
- [.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md](../../../../../.planning/phases/04-in-process-downloader-archive-output/04-CONTEXT.md) — locked decisions
- [.planning/decisions/dev-migration-policy.md](../../../../../.planning/decisions/dev-migration-policy.md) — schema delta convention
