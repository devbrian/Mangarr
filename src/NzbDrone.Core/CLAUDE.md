# NzbDrone.Core

## Purpose

The **core business logic layer** of the application — the largest project by far. Contains the entire domain model, services, scheduled jobs, decision engine, parser, indexer/downloadclient/notification provider plugins, datastore, and event/messaging infrastructure.

Almost every change request that isn't strictly a UI tweak or API DTO change touches code in this project.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\`

## Top-Level Subdirectories (Quick Reference)

Listed by **migration status** post-Phase-17.3 (Sonarr → Mangarr conversion).

### CRITICAL — Core Domain

| Directory | Purpose | Migration Status |
|-----------|---------|------------------|
| `Manga/` | Manga / Chapter / ChapterFile domain (Mangarr canonical) | **Done** — Phase 15 Plan 15-03 deleted `Tv/` (Series.cs / Episode.cs / Season.cs); manga peers landed Phase 2 + Phase 6. See [Manga/CLAUDE.md](./Manga/CLAUDE.md). |
| `Parser/` | Title parsing (manga release regex), `ParsingService` | **Done** — Phase 4 + Phase 6 migrated regex; manga peers under [Parser/Manga/CLAUDE.md](./Parser/Manga/CLAUDE.md). |
| `MetadataSource/` | MangaDex / AniList / MyAnimeList integration | **Done** — Phase 2 shipped `MangaDexMetadataSource`; SkyHook (TVDB) deleted in Phase 15. See [MetadataSource/CLAUDE.md](./MetadataSource/CLAUDE.md). |
| `Profiles/` | TranslationProfile + CustomFormatProfile + Delay + Release profiles | Sonarr Quality profiles dropped (Phase 5 D-04); manga peers shipped Phase 5. See [Profiles/CLAUDE.md](./Profiles/CLAUDE.md), [Profiles/CustomFormats/CLAUDE.md](./Profiles/CustomFormats/CLAUDE.md), [Profiles/Translations/CLAUDE.md](./Profiles/Translations/CLAUDE.md). |

### HIGH — Adaptation

| Directory | Purpose | Migration Status |
|-----------|---------|------------------|
| `Indexers/` | Indexer plugins | **Done** — in-process MangaDex + Comix indexers (Phase 3) RETIRED in Phase 39 (Plan 39-03); `GatewayIndexer` (Phase 37, under `Indexers/Gateway/`) is now the sole `IIndexer`, driving the external manga gateway with zero embedded browser. See [Indexers/CLAUDE.md](./Indexers/CLAUDE.md). |
| `IndexerSearch/` | SearchCriteria classes | Manga peers under [IndexerSearch/Manga/CLAUDE.md](./IndexerSearch/Manga/CLAUDE.md). |
| `MediaFiles/` | Disk scan, file import, organize, rename (CBZ/CBR + image folders) | Phase 15 renamed `EpisodeFile.cs` → `ChapterFile.cs`; Phase 17.3 Plan 17.3-05 D-06 deleted `SeasonPackUpgradeType.cs` vertical. See [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md), [MediaFiles/MangaImport/CLAUDE.md](./MediaFiles/MangaImport/CLAUDE.md), [MediaFiles/ChapterArchiving/CLAUDE.md](./MediaFiles/ChapterArchiving/CLAUDE.md). |
| `DecisionEngine/` | ~32 specifications | Manga peers under [DecisionEngine/Manga/CLAUDE.md](./DecisionEngine/Manga/CLAUDE.md); [DecisionEngine/CLAUDE.md](./DecisionEngine/CLAUDE.md) covers shared infra. |
| `CustomFormats/` | User-defined release scoring | Reusable pattern; manga specs under [CustomFormats/Specifications/Manga/CLAUDE.md](./CustomFormats/Specifications/Manga/CLAUDE.md). |
| `ImportLists/` | External lists (manga-shape substrate) | Phase 26 Plan 26-04 shipped the substrate backend (IL-02/03/06; IMangaImportList contract + 2 base classes + 3 D-13 separate Dapper repos + sync command/service + IHandle&lt;MangaDeletedEvent&gt; auto-add); zero production providers ship per D-08 — Phase 27 plugs in MangaDex / AniList / MyAnimeList plugins additively. See [ImportLists/CLAUDE.md](./ImportLists/CLAUDE.md). |
| `Organizer/` | Filename/folder builder | Manga peers under [Organizer/Manga/CLAUDE.md](./Organizer/Manga/CLAUDE.md). |

### MEDIUM — Minor Adaptation

| Directory | Purpose | Notes |
|-----------|---------|-------|
| `Download/` | Download client integrations + lifecycle | The in-process `InProcessImageDownloadClient` vertical (Phase 4/6) was RETIRED in Phase 39 (Plans 39-01/02); `GatewayDownloadClient` (Phase 38, under `Download/Clients/Gateway/`) is now the sole download client. The Sonarr Usenet/Torrent client infrastructure is reference-preserved. See [Download/CLAUDE.md](./Download/CLAUDE.md). |
| `History/` | Grab/import history | Entity references change. |
| `AutoTagging/` | Rule-based auto-tagging | **Phase 24 RESTORE-REBUILD** — Phase 15 Plan 15-10 DELETED the subtree; Phase 24 restored from `git show 6f857ba0e^` with mechanical Series→Manga + 11-spec manga-shape catalog (8 Sonarr ports - 1 OriginalLanguage drop + 1 QualityProfile split + 3 manga-NEW: AuthorArtist/Demographic/ContentRating). See [AutoTagging/CLAUDE.md](./AutoTagging/CLAUDE.md). |
| `Languages/` | Language enum + parsing | Add scanlation-aware terms. |
| `HealthCheck/` | System health checks | Some checks are series-aware. |
| `Extras/` | Subtitle/metadata sidecar files | Manga sidecars (info.json, cover) differ. |
| `DataAugmentation/` | Scene-mapping data | TV-specific now. |
| `CustomFilters/` | Server-side saved filter | Filter targets change. |

### LOW / NONE — Reusable As-Is

| Directory | Purpose |
|-----------|---------|
| `Datastore/` | DB connection, BasicRepository, **manga baseline `001` + sequential migrations through `010` (Phase 39 retire-in-process cleanup is the head)**. See [Datastore/CLAUDE.md](./Datastore/CLAUDE.md) |
| `Messaging/` | EventAggregator, Commands, Events. See [Messaging/CLAUDE.md](./Messaging/CLAUDE.md) |
| `Notifications/` | 25+ providers (Discord/Slack/Email/Telegram/etc.). See [Notifications/CLAUDE.md](./Notifications/CLAUDE.md) |
| `Authentication/` | User accounts |
| `Configuration/` | ConfigService key/value |
| `Tags/` | Tag entity |
| `RootFolders/` | Library root paths |
| `Jobs/` | Scheduler / TaskManager |
| `Housekeeping/` | Periodic cleanup |
| `Backup/` | DB backup |
| `Update/` | Self-update from cloud |
| `Lifecycle/` | App start/stop |
| `MediaCover/` | Poster/banner caching |
| `Queue/` | Live download tracking |
| `Blocklisting/` | Blocked releases |
| `RemotePathMappings/` | Reverse-proxy path translation |
| `DiskSpace/` | Free space monitoring |
| `Security/` | Cert validation |
| `Localization/` | UI string lookup |
| `Validation/` | FluentValidation rules |
| `ThingiProvider/` | Generic plugin/provider base |
| `Analytics/` | Optional usage telemetry |
| `Annotations/` | Custom attributes |
| `Exceptions/` | Domain exceptions. Note: `SeriesNotFoundException.cs` deleted in Phase 17.3 Plan 17.3-03 D-05; `MetadataSource/MangaNotFoundException.cs` is the manga peer (shipped Phase 2). |
| `Instrumentation/` | NLog setup |
| `Http/` | HTTP convenience |
| `ProgressMessaging/` | Progress events |

## Domain Models (`Manga/`)

Sonarr's `Tv/` directory was deleted in Phase 15 Plan 15-03 atomic cutover.
The manga peers below are the canonical home; `Tv/Series.cs` / `Tv/Episode.cs`
/ `Tv/Season.cs` no longer exist on disk. See [Manga/CLAUDE.md](./Manga/CLAUDE.md)
for the full Manga directory contents.

| File | Purpose |
|------|---------|
| `Manga/Manga.cs` | Main aggregate root. Mirrors Sonarr's `Tv/Series.cs` shape (Pattern S2 markers preserved per D-09). |
| `Manga/Chapter.cs` | Chapter entity. Sonarr-mirror of `Episode.cs`; no scene numbering, no `Runtime`, no `FinaleType` (manga has no airing concept). |
| (no `Season` peer) | PROJECT.md Volumes/Seasons Out-of-Scope; `Chapter.VolumeNumber` is display-only with no Volumes table. |
| `Manga/MangaService.cs` | Manga CRUD, lookup |
| `Manga/ChapterService.cs` (under `Manga/Chapter/` or sibling) | Chapter CRUD, monitor toggling |
| `Manga/RefreshMangaService.cs` | Sync metadata from external source (MangaBaka / MangaDex / AniList / MAL). Auto-relinks a manga whose active-primary cross-source ID is missing (e.g. added under MangaDex, then MangaBaka promoted) via `TryRelinkPrimaryId` — title search + `CrossSourceIdResolver` confirm + fill-null ID carry-over + persist, so libraries heal themselves on primary swap; no confident match → skip + Warn. |
| `Manga/AddMangaService.cs` | Add-new-manga workflow |
| `Manga/MangaEditedService.cs` | Apply post-edit side effects |
| `Manga/MangaRepository.cs` / `ChapterRepository.cs` | Dapper-based repos |
| `Manga/MangaAddedHandler.cs` / `MangaScannedHandler.cs` | IHandle event handlers |
| `Manga/MangaPathBuilder.cs` | Compute manga folder path from naming config |
| `Manga/MangaTitleNormalizer.cs` | Normalize titles for matching |
| `Manga/Commands/` | Manga-related commands (RefreshMangaCommand, etc.) |
| `Manga/Events/` | Manga events (MangaAddedEvent, MangaDeletedEvent, etc.) |

Note: `MoveSeriesService.cs` / `MoveSeriesModal.tsx` deleted in Phase 17.3
Plan 17.3-11 D-11; v1.x GH follow-up issue tracks the dedicated
`MoveMangaModal` design.

## Parser (`Parser/`)

| File | Purpose |
|------|---------|
| `Parser.cs` | **~71 KB** — central regex-based title parser. Static class. |
| `ParsingService.cs` | Maps `ParsedEpisodeInfo` → `RemoteEpisode` (resolves Series/Episodes from DB) |
| `QualityParser.cs` | Extract quality from title |
| `LanguageParser.cs` | Extract language(s) |
| `ReleaseGroupParser.cs` | Extract release group / scanlation group |
| `Model/ReleaseInfo.cs` | DTO from indexer feed |
| `Model/RemoteEpisode.cs` | ReleaseInfo + matched Series + Episodes |
| `Model/ParsedEpisodeInfo.cs` | Output of regex parse |

`Parser.cs` public API (Sonarr-shape kept verbatim per Phase 4 + Phase 6
manga regex addition; manga-specific peers under `Parser/Manga/`):
`ParsePath`, `SimplifyTitle`, `ParseTitle`, `ParseSeriesName`,
`CleanSeriesTitle` (extension), `NormalizeEpisodeTitle`, `NormalizeTitle`,
`NormalizeImdbId`, `RemoveFileExtension`, `HasMultipleLanguages`.

## Decision Engine (`DecisionEngine/`)

| File | Purpose |
|------|---------|
| `DownloadDecisionMaker.cs` | Orchestrator. `GetRssDecision` and `GetSearchDecision` run all specs |
| `DownloadDecision.cs` | Result (Approved + Rejections) |
| `DownloadSpecDecision.cs` | Single-spec result (Accept/Reject + reason) |
| `DownloadDecisionComparer.cs` | Sort comparer for ranking approved decisions |
| `Rejection.cs` | Captured rejection (reason + type) |
| `Specifications/` | ~32 specification classes |
| `Specifications/Search/` | Search-specific specs |
| `Specifications/RssSync/` | RSS-only specs |

**Key spec examples**: `MonitoredEpisodeSpecification`, `QualityAllowedByProfileSpecification`, `UpgradableSpecification`, `BlocklistSpecification`, `CutoffSpecification`, `AcceptableSizeSpecification`, `TorrentSeedingSpecification`, `CustomFormatAllowedByProfileSpecification`, `AnimeVersionUpgradeSpecification`.

See [DecisionEngine/CLAUDE.md](./DecisionEngine/CLAUDE.md) for complete spec list and pattern.

## Indexers (`Indexers/`)

**Phase 39 (Plan 39-03) retired the in-process site-scraper indexers.** `Indexers/Gateway/GatewayIndexer.cs` (Phase 37) is now the sole `IIndexer` — Mangarr fans search/recent requests out to the external manga gateway and never runs an embedded browser. The in-process `MangaDexIndexer` + `ComixIndexer` dirs (and the `ComixPlaywrightSigner`/`IComixSigner` anti-bot signer stack) were deleted; `IndexerFactory.SeededIndexerImplementations` seeds only `GatewayIndexer`. The Sonarr Usenet/Torrent indexer infrastructure (`Newznab/`, `Torznab/`, `Nyaa/`, etc.) is reference-preserved fork heritage, not a manga source.

| Subdirectory | Purpose |
|-------------|---------|
| `IIndexer.cs` / `IndexerBase.cs` / `HttpIndexerBase.cs` | Provider base classes |
| `Gateway/` | `GatewayIndexer` — the sole `IIndexer` (external manga gateway client; Phase 37) |
| `IndexerSourceStatus*` | Per-`SourceKey` escalation ladder (gateway-written; survived Phase 39) |
| `FetchAndParseRssService.cs` | Periodic RSS sync |
| `IndexerFactory.cs` | ThingiProvider factory (seeds `GatewayIndexer` only) |

See [Indexers/CLAUDE.md](./Indexers/CLAUDE.md).

## Download (`Download/`)

**Phase 39 (Plans 39-01/02) retired the in-process image-download vertical** (`Clients/InProcess/`: `InProcessImageDownloadClient` + `ChapterDownloadService` + `ChapterPageFetcher` + `ChapterDownloadState` + housekeeper/poller + the orphaned ChapterArchiving archiver set). `Download/Clients/Gateway/GatewayDownloadClient.cs` (Phase 38) is now the sole download client — it submits an opaque handle back to the external gateway and imports the finished CBZ the gateway delivers. `DownloadClientFactory`'s fresh-DB auto-seed override was deleted (Sonarr-canonical empty download-client list).

| Subdirectory | Purpose |
|-------------|---------|
| `IDownloadClient.cs` / `DownloadClientBase.cs` | Provider base |
| `Clients/Gateway/` | `GatewayDownloadClient` — the sole download client (external gateway; Phase 38) |
| `Clients/` (Usenet/Torrent) | qBittorrent, Transmission, SABnzbd, etc. — reference-preserved fork heritage |
| `Manga/` | Manga monitoring/import handoff survivors (`MangaCompletedDownloadService`, `AutoRetryOrchestrator`, etc.). See [Download/Manga/CLAUDE.md](./Download/Manga/CLAUDE.md). |
| `CompletedDownloadService.cs` | Detect & process completion |
| `DownloadService.cs` | Submit a release to a client |
| `DownloadClientProvider.cs` | Pick which client to use |
| `TrackedDownloads/` | Track in-flight downloads |
| `History/` | Per-download history |
| `Pending/` | Releases waiting for delay profile / cooldown |

See [Download/CLAUDE.md](./Download/CLAUDE.md).

## Media Files (`MediaFiles/`)

| File / Subdirectory | Purpose |
|--------------------|---------|
| `ChapterFile.cs` | Physical file entity (renamed from `EpisodeFile.cs` in Phase 15) — carries `ScanlationGroup` (manga-domain canonical release-group axis per Phase 16.1 D-04) + `TranslatedLanguage` (BCP-47 string). |
| `DiskScanService.cs` | Library scan |
| `MangaImport/` | File import pipeline + import specs (renamed from `EpisodeImport/` in Phase 15) |
| `MangaImport/Specifications/` | NotSampleSpec, MatchesFolderSpec, etc. |
| `RenameChapterFileService.cs` | Rename based on naming config |
| `ChapterFileMovingService.cs` | Move/copy on import |
| `MediaFileDeletionService.cs` | Safe delete |
| `MediaInfo/` | Probe codec/resolution metadata (TV-shape; manga uses image-page metadata under ChapterArchiving/) |
| `ChapterArchiving/` | Manga page-archive lifecycle |

Note: `SeasonPackUpgradeType.cs` deleted in Phase 17.3 Plan 17.3-05 D-06
(manga has no season packs); cascade removed `IConfigService.SeasonPackUpgrade`
+ `MediaManagementSettingsResource.SeasonPackUpgrade*` + openapi.json schema
+ frontend `Settings/MediaManagement/MediaManagement.tsx` form section + 12
en.json keys.

See [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md).

## Datastore (`Datastore/`)

| File / Subdirectory | Purpose |
|--------------------|---------|
| `ModelBase.cs` | Tiny base: `{ int Id }` |
| `BasicRepository.cs` | Generic Dapper repo. Methods: `All`, `Get`, `Find`, `Insert`, `InsertMany`, `Update`, `UpdateMany`, `Upsert`, `Delete`, `DeleteMany`, `SetFields`, `Purge`, `GetPaged`, `Single`, `SingleOrDefault`, `HasItems`, `Count` |
| `IBasicRepository.cs` | Repo interface |
| `Database.cs`, `DbFactory.cs`, `ConnectionStringFactory.cs` | DB setup |
| `Migration/` | Manga baseline `001_mangarr_baseline.cs` + sequential migrations through `010_v1_3_retire_in_process_cleanup.cs` (the Phase 39 head — deletes the orphan in-process provider rows + drops `ChapterDownloadState`) |
| `Converters/` | Dapper / JSON converters (Quality, Languages, OsPath, etc.) |
| `Extensions/` | Mapping extensions |
| `Events/` | DB events |

See [Datastore/CLAUDE.md](./Datastore/CLAUDE.md).

## Messaging (`Messaging/`)

| File / Subdirectory | Purpose |
|--------------------|---------|
| `Events/IEventAggregator.cs` | Single method: `PublishEvent<TEvent>(TEvent @event)` |
| `Events/IHandle.cs` | Subscriber interface (`IHandle<TEvent>` / `IHandleAsync<TEvent>`) |
| `Commands/` | Command base class + executor pattern |

See [Messaging/CLAUDE.md](./Messaging/CLAUDE.md).

## Other Notable Directories

| Directory | Purpose |
|-----------|---------|
| `Notifications/` | 25+ providers; see [Notifications/CLAUDE.md](./Notifications/CLAUDE.md) |
| `CustomFormats/` | Spec-based release scoring; see [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md) |
| `Profiles/Qualities/` | Quality profile entity + items |
| `Profiles/Delay/` | Wait-N-hours-for-better-quality |
| `Profiles/Releases/` | Preferred / required / ignored terms |
| `Qualities/` | Quality enum, QualityDefinition |
| `Languages/` | Language enum, IsoLanguages, LanguageParser |
| `MetadataSource/MangaDex/`, `MetadataSource/AniList/`, `MetadataSource/MyAnimeList/` | Manga metadata source ports (Sonarr `MetadataSource/SkyHook/` deleted in Phase 15) |
| `ImportLists/AniList/`, `ImportLists/Custom/` | List sources (full vertical reference-preserved under `.planning/reference/sonarr-vertical-slices/import-lists/`) |
| `HealthCheck/Checks/` | Individual `XCheck.cs` classes |
| `Housekeeping/Housekeepers/` | One class per cleanup task |
| `Update/` | Self-update from cloud |
| `Authentication/User.cs`, `UserService.cs` | UI account |
| `Backup/BackupService.cs` | Auto + manual backups |
| `Organizer/Manga/MangaFileNameBuilder.cs` | Token-based filename builder for manga (`{Manga Title}.c{chapter:00.000}` etc.); Sonarr-canonical `Organizer/FileNameBuilder.cs` retained for shared infra |
| `ThingiProvider/ProviderBase.cs` | Generic plugin base for indexers/clients/etc. |

## Key Interfaces Cheat Sheet

```csharp
// Domain services (manga peers post-Phase-15)
IMangaService, IChapterService, IChapterFileService
IParsingService          // Map ReleaseInfo → RemoteChapter

// Plugin providers
IIndexer                 // Fetch / Search releases
IDownloadClient          // Submit + monitor downloads
INotification            // Send notifications
IImportList              // Pull external lists
IProvideMangaInfo        // Metadata source (MangaDex / AniList / MAL)
ISearchForNewManga

// Decision engine
IDownloadDecisionEngineSpecification     // single decision rule
IMakeDownloadDecision                    // orchestrator

// Custom format specs
ICustomFormatSpecification               // per-format rule

// Repositories
IBasicRepository<TModel>                 // generic CRUD

// Eventing
IEventAggregator                         // PublishEvent<TEvent>
IHandle<TEvent> / IHandleAsync<TEvent>   // subscriber

// Commands
IExecute<TCommand>                       // Command handler
```

## Data Flow (Search → Library)

```
Indexer.Fetch() / Search()
    ↓ List<ReleaseInfo>
Parser.ParseTitle() → ParsedChapterInfo
    ↓
ParsingService.Map() → RemoteChapter (links Manga + Chapters)
    ↓
DownloadDecisionMaker runs all Specifications
    ↓ List<DownloadDecision> (Approved / Rejected)
DownloadDecisionComparer ranks approved
    ↓
DownloadService.DownloadReport() → IDownloadClient.Download()
    ↓
TrackedDownloadService monitors
    ↓
CompletedDownloadService detects completion
    ↓
ImportApprovedChapters runs ImportSpecs
    ↓ ChapterFile created, file moved/renamed
Events published (ChapterImportedEvent, …)
    ↓
Notifications fire + SignalR broadcasts to UI
```

## Key Patterns

### Lazy Loading (Dapper navigation)
```csharp
public LazyLoaded<Manga> Manga { get; set; }
public LazyLoaded<List<Chapter>> Chapters { get; set; }
// Accessing .Value triggers a query if not yet loaded
```

### Event Publishing
```csharp
_eventAggregator.PublishEvent(new MangaAddedEvent(manga));
// All IHandle<MangaAddedEvent> implementations are invoked
```

### Specification Pattern
```csharp
public class MySpecification : IDownloadDecisionEngineSpecification
{
    public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, SearchCriteriaBase searchCriteria)
    {
        if (badCondition)
            return DownloadSpecDecision.Reject(DownloadRejectionReason.X, "Reason");
        return DownloadSpecDecision.Accept();
    }
}
```

### Provider Plugin
```csharp
public class MyIndexer : HttpIndexerBase<MyIndexerSettings>
{
    public override string Name => "MyIndexer";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
    // implement Fetch / Test
}
```

## Adding a New Specification (DecisionEngine)

1. Create class in `DecisionEngine/Specifications/MyNewSpecification.cs` implementing `IDownloadDecisionEngineSpecification`.
2. Set `Priority` (Default / Low / High) and `Type` (`RejectionType.Permanent` or `Temporary`).
3. Auto-discovered — no DI registration needed.
4. Add unit test in `NzbDrone.Core.Test/DecisionEngineTests/`.

## Adding a New Indexer / DownloadClient / Notification

1. Create folder under `Indexers/MySite/` (or `Download/Clients/MyClient/`, `Notifications/MyService/`).
2. Add `Settings`, provider class, optionally a `Request`/`Parser` class.
3. Inherit appropriate base (`HttpIndexerBase<>`, `DownloadClientBase<>`, `NotificationBase<>`).
4. Auto-discovered. Provide `DefaultDefinitions` if you ship preset configs.
5. Add tests under `NzbDrone.Core.Test/<Area>Tests/`.

## Database Migration

Add a migration when changing schema:

**Pre-v1 dev-migration policy:** During v0.x dev, schema changes edit
`001_mangarr_baseline.cs` in place (per `.planning/memory/project_dev_migration_policy.md`).
Fresh DB required to pick up schema changes. Once v1.0.0 tag ships, this
flips to sequential migration files:

```csharp
// NzbDrone.Core/Datastore/Migration/002_my_new_change.cs (illustrative example, post-v1.0.0 only)
[Migration(2)]
public class my_new_change : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("Manga").AddColumn("MyNewField").AsString().Nullable();
    }
}
```

Migrations run automatically on startup.

## Manga Adaptation Focus (post-Phase-17.3)

### Migration Status (Phase 2-17.3 close-out)
1. **Manga/** — **Done** (Phase 15 Plan 15-03 deleted `Tv/`; manga aggregate root + Chapter + ChapterFile in canonical positions; Phase 17.3 Plan 17.3-12 D-13 trimmed frontend Manga.ts TV-shape carry-overs).
2. **Parser/** — **Done** (Phase 4 + Phase 6 manga regex shipped; `Parser/Manga/` peers).
3. **MetadataSource/** — **Done** (`IProvideMangaInfo` implemented by `MangaDexMetadataSource` + AniList + MyAnimeList; SkyHook deleted Phase 15).
4. **Indexers/** — **Done** (in-process `MangaDexIndexer` + `ComixIndexer` (Phase 3) RETIRED Phase 39 Plan 39-03; `GatewayIndexer` at `Indexers/Gateway/` (Phase 37) is the sole `IIndexer` — external gateway, zero embedded browser).
5. **MediaFiles/** — **Done** (`ChapterFile.cs` + `MangaImport/` pipeline shipped Phase 6/15; `SeasonPackUpgradeType.cs` vertical deleted Phase 17.3 Plan 17.3-05 D-06).
6. **Qualities/** — **Done — dropped** (Phase 5 D-04 retired TV quality model; TranslationProfile + Custom Formats are the manga peer).

### Reusable As-Is
- Decision engine architecture (specifications)
- Download client integration (the in-process image downloader was RETIRED Phase 39 Plans 39-01/02; `GatewayDownloadClient` at `Download/Clients/Gateway/` is the sole download client — external gateway delivers finished CBZs)
- Event messaging system
- Database abstraction & migration pipeline (pre-v1 dev-migration policy: edit `001_mangarr_baseline.cs` in place)
- Custom format system (architecture, not values)
- Notification system (Komga + Kavita live; rest reference-preserved)
- HealthCheck framework
- Backup / Update / Authentication / Tags

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Overall architecture
- [Manga/CLAUDE.md](./Manga/CLAUDE.md) — Domain models (Manga + Chapter + ChapterFile)
- [Parser/CLAUDE.md](./Parser/CLAUDE.md) — Title parsing
- [DecisionEngine/CLAUDE.md](./DecisionEngine/CLAUDE.md) — Decision rules
- [Indexers/CLAUDE.md](./Indexers/CLAUDE.md) — Indexer plugins
- [Download/CLAUDE.md](./Download/CLAUDE.md) — Download clients
- [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md) — File pipeline
- [MetadataSource/CLAUDE.md](./MetadataSource/CLAUDE.md) — Metadata providers
- [Datastore/CLAUDE.md](./Datastore/CLAUDE.md) — DB & migrations
- [Notifications/CLAUDE.md](./Notifications/CLAUDE.md) — Notification providers
- [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md) — Custom formats
- [Profiles/CLAUDE.md](./Profiles/CLAUDE.md) — TranslationProfile / CustomFormatProfile / Delay / Release profiles
- [Messaging/CLAUDE.md](./Messaging/CLAUDE.md) — Events & Commands
- [ImportLists/CLAUDE.md](./ImportLists/CLAUDE.md) — Import lists (infrastructure live; concrete providers reference-preserved)
- [../Mangarr.Api.V5/CLAUDE.md](../Mangarr.Api.V5/CLAUDE.md) — API controllers
- [../Mangarr.Http/CLAUDE.md](../Mangarr.Http/CLAUDE.md) — HTTP infrastructure
