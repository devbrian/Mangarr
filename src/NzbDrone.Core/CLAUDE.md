# NzbDrone.Core

## Purpose

The **core business logic layer** of the application — the largest project by far. Contains the entire domain model, services, scheduled jobs, decision engine, parser, indexer/downloadclient/notification provider plugins, datastore, and event/messaging infrastructure.

Almost every change request that isn't strictly a UI tweak or API DTO change touches code in this project.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\`

## Top-Level Subdirectories (Quick Reference)

Listed by **migration priority** for the Sonarr → Mangarr conversion.

### CRITICAL — Core Domain (Major Rewrite Required)

| Directory | Purpose | Migration Notes |
|-----------|---------|-----------------|
| `Tv/` | Series / Episode / Season domain | Rename: Series→Manga, Episode→Chapter, Season→Volume. **Series.cs already has MalIds + AniListIds.** See [Tv/CLAUDE.md](./Tv/CLAUDE.md). |
| `Parser/` | Title parsing regex (~71 KB), `ParsingService` | Replace anime/TV regex with manga release patterns. See [Parser/CLAUDE.md](./Parser/CLAUDE.md). |
| `MetadataSource/` | TVDB/TMDB integration via SkyHook | Replace with MangaDex / AniList / MangaUpdates / MyAnimeList. See [MetadataSource/CLAUDE.md](./MetadataSource/CLAUDE.md). |
| `Qualities/` | Quality definitions (480p/720p/1080p/etc.) | Replace with manga quality tiers (raw / official / scanlation, DPI). |
| `Profiles/` | Quality + Delay + Release profiles | Reusable architecture; specific qualities need rework. See [Profiles/CLAUDE.md](./Profiles/CLAUDE.md). |

### HIGH — Significant Adaptation

| Directory | Purpose | Migration Notes |
|-----------|---------|-----------------|
| `Indexers/` | Indexer plugins (Newznab, Torznab, Nyaa, TorrentRSS, Custom) | Add manga site scrapers. See [Indexers/CLAUDE.md](./Indexers/CLAUDE.md). |
| `IndexerSearch/` | SearchCriteria classes | New `ChapterSearchCriteria` etc. needed. |
| `MediaFiles/` | Disk scan, file import, organize, rename | CBZ/CBR + image folders instead of video. See [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md). |
| `DecisionEngine/` | ~32 specifications | Many specs reusable; some TV-specific (AirDate, SceneNumbering). See [DecisionEngine/CLAUDE.md](./DecisionEngine/CLAUDE.md). |
| `CustomFormats/` | User-defined release scoring | Reusable pattern; add manga-specific spec types. See [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md). |
| `ImportLists/` | External lists (Trakt, custom) | Need MangaDex/AniList list ingestion. See [ImportLists/CLAUDE.md](./ImportLists/CLAUDE.md). |
| `Organizer/` | Filename/folder builder | New manga naming tokens. |
| `SeriesStats/` | Materialized stats | Episodes→Chapters terminology. |

### MEDIUM — Minor Adaptation

| Directory | Purpose | Notes |
|-----------|---------|-------|
| `Download/` | Download client integrations + lifecycle | See [Download/CLAUDE.md](./Download/CLAUDE.md). Mostly reusable. |
| `History/` | Grab/import history | Entity references change. |
| `AutoTagging/` | Rule-based auto-tagging | Specs adapt. |
| `Languages/` | Language enum + parsing | Add scanlation-aware terms. |
| `HealthCheck/` | System health checks | Some checks are series-aware. |
| `Extras/` | Subtitle/metadata sidecar files | Manga sidecars (info.json, cover) differ. |
| `DataAugmentation/` | Scene-mapping data | TV-specific now. |
| `CustomFilters/` | Server-side saved filter | Filter targets change. |

### LOW / NONE — Reusable As-Is

| Directory | Purpose |
|-----------|---------|
| `Datastore/` | DB connection, BasicRepository, **224 migrations**. See [Datastore/CLAUDE.md](./Datastore/CLAUDE.md) |
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
| `Exceptions/` | Domain exceptions |
| `Instrumentation/` | NLog setup |
| `Http/` | HTTP convenience |
| `ProgressMessaging/` | Progress events |

## Domain Models (`Tv/`)

| File | Purpose |
|------|---------|
| `Series.cs` | Main entity (40 props). Already has `MalIds`, `AniListIds` for manga migration. |
| `Episode.cs` | Episode entity. 21 properties incl. scene numbering, `AirDate`, `Runtime`, `FinaleType`. |
| `Season.cs` | Embedded document (`IEmbeddedDocument`). Just `{SeasonNumber, Monitored, Images}`. |
| `SeriesService.cs` | Series CRUD, lookup |
| `EpisodeService.cs` | Episode CRUD, monitor toggling |
| `RefreshSeriesService.cs` | Sync metadata from external source (SkyHook/TVDB) |
| `AddSeriesService.cs` | Add-new-series workflow |
| `MoveSeriesService.cs` | File move operations |
| `SeriesEditedService.cs` | Apply post-edit side effects |
| `SeriesRepository.cs` / `EpisodeRepository.cs` | Dapper-based repos |
| `SeriesAddedHandler.cs` / `SeriesScannedHandler.cs` | IHandle event handlers |
| `SeriesPathBuilder.cs` | Compute series folder path from naming config |
| `SeriesTitleNormalizer.cs` | Normalize titles for matching |
| `Commands/` | Series-related commands (RefreshSeriesCommand, etc.) |
| `Events/` | Series events (SeriesAddedEvent, SeriesDeletedEvent, etc.) |

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

`Parser.cs` public API: `ParsePath`, `SimplifyTitle`, `ParseTitle`, `ParseSeriesName`, `CleanSeriesTitle` (extension), `NormalizeEpisodeTitle`, `NormalizeTitle`, `NormalizeImdbId`, `RemoveFileExtension`, `HasMultipleLanguages`.

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

| Subdirectory | Purpose |
|-------------|---------|
| `IIndexer.cs` / `IndexerBase.cs` / `HttpIndexerBase.cs` | Provider base classes |
| `Newznab/` | Usenet indexer (RSS + search XML) |
| `Torznab/` | Newznab-compatible torrent indexer |
| `TorrentRss/` | Generic torrent RSS |
| `Nyaa/` | Anime torrent tracker |
| `BroadcastheNet/`, `HDBits/`, `IPTorrents/`, `FileList/`, `Torrentleech/`, `Fanzub/` | Specific trackers |
| `FetchAndParseRssService.cs` | Periodic RSS sync |
| `IndexerFactory.cs` | ThingiProvider factory |

See [Indexers/CLAUDE.md](./Indexers/CLAUDE.md).

## Download (`Download/`)

| Subdirectory | Purpose |
|-------------|---------|
| `IDownloadClient.cs` / `DownloadClientBase.cs` | Provider base |
| `Clients/` | qBittorrent, Transmission, Deluge, SABnzbd, NzbGet, Aria2, etc. |
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
| `EpisodeFile.cs` | Physical file entity |
| `DiskScanService.cs` | Library scan |
| `EpisodeImport/` | File import pipeline + import specs |
| `EpisodeImport/Specifications/` | NotSampleSpec, MatchesFolderSpec, etc. |
| `RenameEpisodeFileService.cs` | Rename based on naming config |
| `EpisodeFileMovingService.cs` | Move/copy on import |
| `MediaFileDeletionService.cs` | Safe delete |
| `MediaInfo/` | Probe codec/resolution metadata |
| `TorrentInfo/` | Torrent file metadata |

See [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md).

## Datastore (`Datastore/`)

| File / Subdirectory | Purpose |
|--------------------|---------|
| `ModelBase.cs` | Tiny base: `{ int Id }` |
| `BasicRepository.cs` | Generic Dapper repo. Methods: `All`, `Get`, `Find`, `Insert`, `InsertMany`, `Update`, `UpdateMany`, `Upsert`, `Delete`, `DeleteMany`, `SetFields`, `Purge`, `GetPaged`, `Single`, `SingleOrDefault`, `HasItems`, `Count` |
| `IBasicRepository.cs` | Repo interface |
| `Database.cs`, `DbFactory.cs`, `ConnectionStringFactory.cs` | DB setup |
| `Migration/` | **224 FluentMigrator migrations** (`000_…` → `223_…`) |
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
| `MetadataSource/SkyHook/` | TVDB API wrapper (Sonarr's hosted shim) |
| `ImportLists/AniList/`, `ImportLists/Custom/` | List sources |
| `HealthCheck/Checks/` | Individual `XCheck.cs` classes |
| `Housekeeping/Housekeepers/` | One class per cleanup task |
| `Update/` | Self-update from cloud |
| `Authentication/User.cs`, `UserService.cs` | UI account |
| `Backup/BackupService.cs` | Auto + manual backups |
| `Organizer/FileNameBuilder.cs` | Token-based filename builder (`{Series Title}.S{season:00}E{episode:00}.{Quality Title}`) |
| `ThingiProvider/ProviderBase.cs` | Generic plugin base for indexers/clients/etc. |

## Key Interfaces Cheat Sheet

```csharp
// Domain services
ISeriesService, IEpisodeService, IEpisodeFileService
IParsingService          // Map ReleaseInfo → RemoteEpisode

// Plugin providers
IIndexer                 // Fetch / Search releases
IDownloadClient          // Submit + monitor downloads
INotification            // Send notifications
IImportList              // Pull external lists
IProvideSeriesInfo       // Metadata source (TVDB / AniList / etc.)
ISearchForNewSeries

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
Parser.ParseTitle() → ParsedEpisodeInfo
    ↓
ParsingService.Map() → RemoteEpisode (links Series + Episodes)
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
ImportApprovedEpisodes runs ImportSpecs
    ↓ EpisodeFile created, file moved/renamed
Events published (EpisodeImportedEvent, …)
    ↓
Notifications fire + SignalR broadcasts to UI
```

## Key Patterns

### Lazy Loading (Dapper navigation)
```csharp
public LazyLoaded<Series> Series { get; set; }
public LazyLoaded<List<Episode>> Episodes { get; set; }
// Accessing .Value triggers a query if not yet loaded
```

### Event Publishing
```csharp
_eventAggregator.PublishEvent(new SeriesAddedEvent(series));
// All IHandle<SeriesAddedEvent> implementations are invoked
```

### Specification Pattern
```csharp
public class MySpecification : IDownloadDecisionEngineSpecification
{
    public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
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

```csharp
// src/NzbDrone.Core/Datastore/Migration/224_my_new_change.cs
[Migration(224)]
public class my_new_change : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Alter.Table("Series").AddColumn("MyNewField").AsString().Nullable();
    }
}
```

Number sequentially after the highest existing migration. Migrations run automatically on startup.

## Manga Adaptation Focus

### High Priority Changes
1. **Tv/** — Rename Series→Manga, Episode→Chapter, Season→Volume (already partially done — MalIds/AniListIds exist on Series.cs)
2. **Parser/** — New regex patterns for manga release naming (chapter, scanlation group, etc.)
3. **MetadataSource/** — Implement `IProvideSeriesInfo` for MangaDex/AniList; deprecate or wrap SkyHook
4. **Indexers/** — Add manga site scrapers (e.g., MangaDex feeds, manga torrent trackers)
5. **MediaFiles/** — Handle CBZ/CBR/folder of images instead of video files
6. **Qualities/** — Replace TV quality enum with manga scan tiers

### Reusable As-Is
- Decision engine architecture (specifications)
- Download client integration (qBittorrent et al. work for any payload)
- Event messaging system
- Database abstraction & migration pipeline
- Quality profile system (architecture, not values)
- Notification system (just update message text)
- HealthCheck framework
- Backup / Update / Authentication / Tags

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Overall architecture
- [Tv/CLAUDE.md](./Tv/CLAUDE.md) — Domain models
- [Parser/CLAUDE.md](./Parser/CLAUDE.md) — Title parsing
- [DecisionEngine/CLAUDE.md](./DecisionEngine/CLAUDE.md) — Decision rules
- [Indexers/CLAUDE.md](./Indexers/CLAUDE.md) — Indexer plugins
- [Download/CLAUDE.md](./Download/CLAUDE.md) — Download clients
- [MediaFiles/CLAUDE.md](./MediaFiles/CLAUDE.md) — File pipeline
- [MetadataSource/CLAUDE.md](./MetadataSource/CLAUDE.md) — Metadata providers
- [Datastore/CLAUDE.md](./Datastore/CLAUDE.md) — DB & migrations
- [Notifications/CLAUDE.md](./Notifications/CLAUDE.md) — Notification providers
- [CustomFormats/CLAUDE.md](./CustomFormats/CLAUDE.md) — Custom formats
- [Profiles/CLAUDE.md](./Profiles/CLAUDE.md) — Quality/Delay/Release profiles
- [Messaging/CLAUDE.md](./Messaging/CLAUDE.md) — Events & Commands
- [ImportLists/CLAUDE.md](./ImportLists/CLAUDE.md) — Import lists
- [../Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) — API controllers
- [../Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) — HTTP infrastructure
