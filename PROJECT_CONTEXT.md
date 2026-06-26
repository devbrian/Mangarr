# PROJECT_CONTEXT.md

> **HIGH PRIORITY: DOCUMENTATION MAINTENANCE**
>
> As you work on this project, you MUST keep documentation up to date:
>
> 1. **Update existing CLAUDE.md files** when you modify code in that directory
> 2. **Create new CLAUDE.md files** when adding new directories or major features
> 3. **Update this PROJECT_CONTEXT.md** when making architectural changes
> 4. **Update cross-references** when renaming or moving files
> 5. **Document Sonarr→Mangarr changes** as migrations are completed

---

## Overview

**Mangarr** is a manga/manhwa/manhua library manager forked from **Sonarr** (a TV-show automation tool). This document is the architectural deep-dive used to orient any future contributor or AI agent working on the codebase. It complements the high-level [CLAUDE.md](./CLAUDE.md), focusing on data flow, patterns, and design rationale.

The application is a self-hosted background service exposing a web UI at port 8989, with a comprehensive REST API + SignalR push for real-time UI updates.

## Technology Stack

| Layer | Technology | Notes |
|-------|------------|-------|
| Backend Runtime | .NET 10.0 (C#) | Single-process service |
| Frontend | React 18.3 + TypeScript 5.7 | SPA, served by backend |
| Database | SQLite (default) / PostgreSQL | One DB for main + one for logs |
| ORM | Dapper | Hand-rolled mapping; raw SQL for queries |
| Schema migrations | FluentMigrator | Sequential `001_mangarr_baseline.cs` → `017_…` (post-v1.0.0 policy: append a new sequential migration; the pre-v1 edit-baseline-in-place rule no longer applies) |
| State Management | Redux 4 + Zustand 5 + TanStack React Query 5.61 | Tri-store architecture |
| Real-time | SignalR 10 | `/signalr/messages` hub |
| Build | MSBuild + Webpack 5 | Yarn 1.22 (classic, pinned via `package.json` `packageManager`) |
| Testing | NUnit | Unit / Integration / Automation |
| DI Container | DryIoc | Convention-based registration |
| Logging | NLog | File + DB targets, sensitive-data scrubbing |
| Validation | FluentValidation | Resource & rule validators |
| HTTP Client | Custom wrapper around HttpClient | Proxy/cache aware, fluent builder |

## Directory Structure (Annotated)

```
Mangarr/
├── src/                              # Backend C# (Mangarr.sln)
│   ├── NzbDrone.Common/              # Foundational utilities, NO references to other NzbDrone projects
│   │   ├── Disk/                     # IDiskProvider, OsPath, file ops abstraction
│   │   ├── Http/                     # HTTP client wrapper, request builder, dispatchers
│   │   ├── Cache/                    # In-memory caching (CacheManager)
│   │   ├── Composition/              # Assembly scanning for DI
│   │   ├── EnvironmentInfo/          # OS / runtime / app paths
│   │   ├── Instrumentation/          # NLog setup, layout, sensitive data scrubbing
│   │   ├── Serializer/               # Newtonsoft.Json + System.Text.Json converters
│   │   ├── Processes/                # Run external processes
│   │   ├── TPL/                      # Debouncer, RateLimit, LockByIdPool, Schedulers
│   │   ├── EnsureThat/               # Fluent validation
│   │   └── Extensions/               # LINQ / String / Path / Exception helpers
│   ├── NzbDrone.Core/                # Business logic — see src/NzbDrone.Core/CLAUDE.md
│   │   ├── Manga/                    # CRITICAL: Manga / Chapter domain (Sonarr Tv/ deleted Phase 15 Plan 15-03; no Season peer)
│   │   ├── Parser/                   # CRITICAL: Title regex; manga peers under Parser/Manga/
│   │   ├── DecisionEngine/           # Manga decision specs under DecisionEngine/Manga/
│   │   ├── Indexers/                 # GatewayIndexer (sole IIndexer; in-process MangaDex/Comix scrapers retired Phase 39)
│   │   ├── IndexerSearch/            # SearchCriteria classes
│   │   ├── Download/                 # GatewayDownloadClient (sole client; in-process image downloader retired Phase 39) + lifecycle
│   │   ├── MediaFiles/               # Disk scan, file import, organize, delete; ChapterFile.cs
│   │   ├── MetadataSource/           # MangaBaka / MangaDex / AniList / MAL (SkyHook/TVDB deleted Phase 15)
│   │   ├── Datastore/                # DB connection, BasicRepository<T>, Migration/
│   │   ├── Notifications/            # Komga + Kavita live (rest reference-preserved per .planning/reference/)
│   │   ├── CustomFormats/            # User-defined release scoring rules
│   │   ├── Profiles/                 # Quality / Delay / Release profiles
│   │   ├── Qualities/                # Quality enum + definitions
│   │   ├── Languages/                # Language enum + parsing
│   │   ├── ImportLists/              # AniList, Trakt, custom lists
│   │   ├── Messaging/                # Events / Commands infrastructure
│   │   ├── Jobs/                     # Scheduler / TaskManager
│   │   ├── HealthCheck/              # System health checks
│   │   ├── Housekeeping/             # Periodic cleanup
│   │   ├── History/                  # ChapterHistory record (grabs, imports)
│   │   ├── Blocklisting/             # Blocked releases
│   │   ├── Queue/                    # Active download tracking
│   │   ├── AutoTagging/              # Rule-based tag application
│   │   ├── Configuration/            # ConfigService key/value
│   │   ├── Authentication/           # User accounts
│   │   ├── RootFolders/              # Library root paths
│   │   ├── Tags/                     # Tag entity
│   │   ├── Organizer/                # Filename / folder name builder
│   │   ├── Backup/                   # DB backup
│   │   ├── Update/                   # Self-update from cloud
│   │   ├── ThingiProvider/           # Generic plugin/provider base
│   │   ├── Lifecycle/                # App start/stop events
│   │   ├── MediaCover/               # Poster/banner/fanart fetching
│   │   ├── MangaStats/               # Materialized statistics view (Sonarr SeriesStats renamed Phase 15)
│   │   ├── Localization/             # Locale strings
│   │   ├── Validation/               # Validation rules
│   │   └── …                         # More: Analytics, RemotePathMappings, Security, etc.
│   ├── NzbDrone.Host/                # ASP.NET Core hosting
│   │   ├── Bootstrap.cs              # Entry → setup logger, mode, container
│   │   ├── Startup.cs                # ASP.NET Core: ConfigureServices + Configure
│   │   └── AccessControl/            # Firewall / remote access
│   ├── NzbDrone.Console/             # Console entry point
│   │   └── ConsoleApp.cs             # Main(), exception handling, exit codes
│   ├── NzbDrone.SignalR/             # 3 files
│   │   ├── MessageHub.cs             # The SignalR Hub class
│   │   ├── IBroadcastSignalRMessage.cs
│   │   └── SignalRMessage.cs
│   ├── Mangarr.Http/                  # REST infrastructure
│   │   ├── REST/                     # RestController<T>, RestResource, RestControllerWithSignalR
│   │   ├── Authentication/           # Cookie + ApiKey + Basic
│   │   ├── Middleware/               # UrlBase / Logging / Cache / Version / Buffering
│   │   ├── ErrorManagement/          # MangarrErrorPipeline (global exception → JSON)
│   │   ├── Frontend/Mappers/         # Serve index.html, login, static assets, covers
│   │   ├── ClientSchema/             # Dynamic form schema generation (for plugins)
│   │   ├── Validation/               # Custom validators
│   │   └── Ping/                     # /ping health endpoint
│   ├── Mangarr.Api.V5/                # current (and sole) REST API; see file CLAUDE.md (Mangarr.Api.V3 wholesale-deleted Phase 15 Plan 15-06)
│   ├── NzbDrone.Update/              # Self-update binary
│   ├── NzbDrone.Mono/                # Linux/Mac specific (DiskProvider)
│   ├── NzbDrone.Windows/             # Windows specific
│   ├── Mangarr.RuntimePatches/        # Runtime monkey-patches (third-party shims)
│   ├── ServiceHelpers/               # Service install on Windows/macOS
│   ├── Libraries/                    # Vendored DLLs
│   └── *.Test/                       # NUnit projects
├── frontend/                         # See frontend/CLAUDE.md
│   ├── src/                          # 35 top-level dirs
│   └── build/webpack.config.js
├── _output/                          # Build artifacts (UI bundled here)
├── _tests/                           # Test artifacts
├── scripts/                          # Build/test scripts
├── distribution/                     # Packaging
└── docker/                           # Docker assets
```

## Architecture Layers

```
┌───────────────────────────────────────────────────────────┐
│                    Frontend (React)                        │
│   AppRoutes → Pages → Components → useApiQuery/Mutation   │
│              Redux + Zustand + React Query                 │
└──────────────────┬───────────────────────┬────────────────┘
                   │ HTTP REST              │ WebSocket (SignalR)
┌──────────────────┴───────────────────────┴────────────────┐
│                    Mangarr.Api.V5                           │
│         REST controllers, Resource (DTO) classes           │
│  MangaController → Manga → Map → MangaResource             │
└──────────────────────┬─────────────────────────────────────┘
                       │
┌──────────────────────┴─────────────────────────────────────┐
│                    Mangarr.Http                              │
│  RestController<T> base • Middleware • Auth • Validation   │
│  Cookie/ApiKey/Basic auth • UrlBase • Caching • Errors     │
└──────────────────────┬─────────────────────────────────────┘
                       │
┌──────────────────────┴─────────────────────────────────────┐
│                   NzbDrone.Host                             │
│         Bootstrap • Startup (DI + middleware)              │
│           Kestrel server, lifecycle, browser open          │
└──────────────────────┬─────────────────────────────────────┘
                       │
┌──────────────────────┴─────────────────────────────────────┐
│                   NzbDrone.Core                             │
│   ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌────────────────┐  │
│   │Manga(Domain)│Parser │ Indexers  │ DecisionEngine │  │
│   │Manga/Chapter│Regex  │ Search    │ Manga/Specs     │  │
│   └─────────┘ └─────────┘ └──────────┘ └────────────────┘  │
│   ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌────────────────┐  │
│   │ Download│MediaFiles│ Datastore│ Messaging      │  │
│   │ Clients │Import    │ Repo/Migrations │ EventAggregator│
│   └─────────┘ └─────────┘ └──────────┘ └────────────────┘  │
│   ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌────────────────┐  │
│   │Profiles │Qualities │CustomFormats│Notifications   │  │
│   │QP/Delay │Definitions│Specs        │Komga + Kavita  │  │
│   └─────────┘ └─────────┘ └──────────┘ └────────────────┘  │
└──────────────────────┬─────────────────────────────────────┘
                       │
┌──────────────────────┴─────────────────────────────────────┐
│                  NzbDrone.Common                            │
│        Disk • HTTP • Cache • Logging • Serialization       │
│        OsPath • EnsureThat • Process • TPL                 │
└────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────┐
│                  NzbDrone.SignalR (orthogonal)              │
│        MessageHub broadcasts events to all clients         │
└────────────────────────────────────────────────────────────┘
```

## Core Data Flow: Search & Download Pipeline

This is the central pipeline that transforms an indexer feed into an organized library entry. Every Specification, every CustomFormat, every Indexer plugs into this flow.

```
1. Trigger
   ├─ MangaRssSyncService scheduled (recurring)    IndexerSearch/Manga/MangaRssSyncService.cs
   └─ User-initiated chapter search command        IndexerSearch/Manga/ChapterSearchCommand.cs

2. Indexer.Fetch() / Search()                     Indexers/IndexerBase.cs
   → List<ReleaseInfo>                             Parser/Model/ReleaseInfo.cs

3. MangaParser parses the release title            Parser/Manga/MangaParser.cs
   → ParsedChapterInfo                             Parser/Manga/Model/ParsedChapterInfo.cs

4. MangaParsingService.Map(parsedInfo, …)         Parser/Manga/MangaParsingService.cs
   → RemoteChapter                                Parser/Manga/Model/RemoteChapter.cs
     • Manga (matched in DB by title)
     • Chapters (resolved from chapter numbers)
     • ParsedChapterInfo
     • Release info passed through

5. MangaDownloadDecisionMaker.GetDecisions(...)    DecisionEngine/Manga/MangaDownloadDecisionMaker.cs
   For each release, run ALL manga Specifications in order
   (under DecisionEngine/Manga/Specifications/):
   ├─ MangaSpecification
   ├─ ChapterRequestedSpecification
   ├─ AlreadyImportedChapterSpecification
   ├─ BlocklistSpecification
   ├─ AcceptableSizeSpecification / MaximumSizeSpecification
   ├─ MinimumAgeSpecification
   ├─ LanguageInTranslationProfileSpecification
   ├─ CustomFormatMinimumScoreSpecification
   ├─ DeletedChapterFileSpecification
   └─ … (manga decision specs)
   →  MangaDownloadDecision (Approved | Rejected with reason)

6. Sort approved decisions                         DecisionEngine/Manga/MangaDownloadDecisionComparer.cs
   (by translation profile, custom format score, age, size)

7. MangaDownloadService.DownloadReport(decision)    Download/MangaDownloadService.cs
   → IDownloadClient.Download(remoteChapter)

8. Track download                                  Download/TrackedDownloads/TrackedDownloadService.cs

9. CompletedDownloadService monitors completion    Download/CompletedDownloadService.cs

10. ImportApprovedChapters — runs import specs      MediaFiles/MangaImport/ImportApprovedChapters.cs

11. ChapterFile created, file moved/renamed         MediaFiles/ChapterFile.cs
    MangaFileNameBuilder builds path                Organizer/Manga/MangaFileNameBuilder.cs

12. Events published                                Messaging/Events/
    ├─ ChapterImportedEvent                         MediaFiles/MangaImport/ChapterImportedEvent.cs
    └─ DownloadCompletedEvent
    → Notifications subscribe → user alerts
    → SignalR broadcasts → UI updates
```

## Key Architectural Patterns

### 1. Event-Driven via IEventAggregator

```csharp
// Publishing
_eventAggregator.PublishEvent(new SeriesAddedEvent(series));

// Subscribing — implement IHandle<TEvent>
public class MyHandler : IHandle<SeriesAddedEvent>
{
    public void Handle(SeriesAddedEvent message) { /* … */ }
}
// Handlers auto-registered via DI; events fan out asynchronously.
```

`IEventAggregator` (declared in `NzbDrone.Core/Messaging/Events/IEventAggregator.cs`) has just one method: `PublishEvent<TEvent>(TEvent @event) where TEvent : IEvent`. This decouples producers from consumers and is foundational throughout the codebase.

### 2. Specification Pattern (Decision Engine)

Each download rule implements `IDownloadDecisionEngineSpecification`:

```csharp
public class MySpecification : IDownloadDecisionEngineSpecification
{
    public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
    {
        if (someBadCondition)
            return DownloadSpecDecision.Reject(DownloadRejectionReason.X, "Reason text");
        return DownloadSpecDecision.Accept();
    }

    public SpecificationPriority Priority => SpecificationPriority.Default;
    public RejectionType Type => RejectionType.Permanent | RejectionType.Temporary;
}
```

All specs auto-discovered. Composable, independently testable. Same pattern used in:
- Manga decision specs (`DecisionEngine/Manga/Specifications/`)
- Manga import specs (`MediaFiles/MangaImport/Specifications/`)
- CustomFormat specs (`CustomFormats/Specifications/`)
- AutoTag specs (`AutoTagging/Specifications/`)

### 3. Provider/Plugin Pattern (ThingiProvider)

Indexers, DownloadClients, Notifications, ImportLists, MetadataSources all derive from `ProviderBase<TProviderDefinition>`:

```csharp
public abstract class ProviderBase<TDefinition> : IProvider
    where TDefinition : ProviderDefinition
{
    public TDefinition Definition { get; set; }
    public abstract string Name { get; }
    public abstract ProviderMessage Message { get; }
    public abstract IEnumerable<TDefinition> DefaultDefinitions { get; }
}
```

Plugin discovery via reflection in `NzbDrone.Common/Composition/AssemblyLoader.cs`. Settings serialized as JSON in DB. Schema dynamically built for the UI form via `Mangarr.Http/ClientSchema/SchemaBuilder.cs`.

### 4. Repository Pattern with Dapper

```csharp
// Generic base
public class BasicRepository<TModel> : IBasicRepository<TModel> where TModel : ModelBase
{
    public List<TModel> All();
    public TModel Get(int id);
    public TModel Insert(TModel model);
    public TModel Update(TModel model);
    public void Delete(int id);
    public void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
    public PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec);
    // …
}
```

`ModelBase` is just `{ int Id; }`. Embedded documents (e.g. `Season`) implement `IEmbeddedDocument` and serialize as JSON columns. Lazy navigation via `LazyLoaded<T>`.

### 5. Command Pattern

Commands encapsulate user-initiated actions. Executed via a queue with progress tracking:

```csharp
public class RefreshSeriesCommand : Command { public List<int> SeriesIds { get; set; } }

public class RefreshSeriesCommandExecutor : IExecute<RefreshSeriesCommand>
{
    public void Execute(RefreshSeriesCommand message) { /* … */ }
}
```

Frontend issues commands via `POST /api/v5/command`, polls progress, receives completion via SignalR.

## Domain Model Mapping (Sonarr → Mangarr)

Sonarr's `Tv/` domain (`Series`/`Season`/`Episode`/`EpisodeFile`) was deleted in Phase 15 Plan 15-03 and replaced by the manga peers below. There is **no Season peer** — manga use a flat chapter list (PROJECT.md Out-of-Scope; `volumeNumber` is display-only on `Chapter`).

| Mangarr Model | File | Sonarr Origin | Notes |
|-------------|------|---------------|-------|
| `Manga` | [src/NzbDrone.Core/Manga/Manga.cs](./src/NzbDrone.Core/Manga/Manga.cs) | `Series` | MangaDex/MAL/AniList IDs; TranslationProfileId + CustomFormatProfileId |
| `Chapter` | [src/NzbDrone.Core/Manga/Chapter.cs](./src/NzbDrone.Core/Manga/Chapter.cs) | `Episode` | Chapter number, release date; no Season peer |
| `ChapterFile` | [src/NzbDrone.Core/MediaFiles/ChapterFile.cs](./src/NzbDrone.Core/MediaFiles/ChapterFile.cs) | `EpisodeFile` | CBZ/CBR/folder artifact; Languages, CustomFormats |
| `ReleaseInfo` | [src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs](./src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs) | (reusable) | Indexer-agnostic |
| `RemoteChapter` | [src/NzbDrone.Core/Parser/Manga/Model/RemoteChapter.cs](./src/NzbDrone.Core/Parser/Manga/Model/RemoteChapter.cs) | `RemoteEpisode` | Wraps ReleaseInfo + matched Manga/Chapters |
| `ParsedChapterInfo` | [src/NzbDrone.Core/Parser/Manga/Model/ParsedChapterInfo.cs](./src/NzbDrone.Core/Parser/Manga/Model/ParsedChapterInfo.cs) | `ParsedEpisodeInfo` | Output of regex parsing |

### Manga.cs Properties (Current State)

The `Manga` class extends `ModelBase` and includes:

- IDs: **`MangaDexId`**, **`MalId`**, **`AniListId`**
- Profiles: `TranslationProfileId`, `CustomFormatProfileId`, `UpgradeAllowedOverride`
- Metadata: `Title`, `CleanTitle`, `SortTitle`, `AlternativeTitles`, `TitleSlug`, `Overview`, `Genres`, `Artist`, `PrimaryAuthor`, `PublicationYear`, `Demographic`, `ContentRating`
- Status: `Status` (ongoing/completed/hiatus/cancelled/deleted), `Monitored`, `MonitorNewItems`
- Paths: `Path`, `RootFolderPath`
- Dates: `Added`, `LastInfoSync`
- Collections: `Images`, `Tags`
- Counts: `TotalChapterCount`
- Other: `AddOptions`

## Key Files Reference

### Backend Entry Points
- **Solution**: `src/Mangarr.sln`
- **Console main**: [src/NzbDrone.Console/ConsoleApp.cs](./src/NzbDrone.Console/ConsoleApp.cs)
- **Bootstrap**: [src/NzbDrone.Host/Bootstrap.cs](./src/NzbDrone.Host/Bootstrap.cs)
- **ASP.NET Startup (DI + middleware)**: [src/NzbDrone.Host/Startup.cs](./src/NzbDrone.Host/Startup.cs)

### Core Domain
- **Manga model**: [src/NzbDrone.Core/Manga/Manga.cs](./src/NzbDrone.Core/Manga/Manga.cs)
- **Chapter model**: [src/NzbDrone.Core/Manga/Chapter.cs](./src/NzbDrone.Core/Manga/Chapter.cs)
- **ChapterFile**: [src/NzbDrone.Core/MediaFiles/ChapterFile.cs](./src/NzbDrone.Core/MediaFiles/ChapterFile.cs)
- **Manga parser**: [src/NzbDrone.Core/Parser/Manga/MangaParser.cs](./src/NzbDrone.Core/Parser/Manga/MangaParser.cs) — **regex-heavy**
- **Decision Engine**: [src/NzbDrone.Core/DecisionEngine/Manga/MangaDownloadDecisionMaker.cs](./src/NzbDrone.Core/DecisionEngine/Manga/MangaDownloadDecisionMaker.cs)
- **Repository base**: [src/NzbDrone.Core/Datastore/BasicRepository.cs](./src/NzbDrone.Core/Datastore/BasicRepository.cs)
- **ModelBase**: [src/NzbDrone.Core/Datastore/ModelBase.cs](./src/NzbDrone.Core/Datastore/ModelBase.cs) — single `Id` property
- **EventAggregator**: [src/NzbDrone.Core/Messaging/Events/IEventAggregator.cs](./src/NzbDrone.Core/Messaging/Events/IEventAggregator.cs)

### API Layer
- **V5 Controllers**: `src/Mangarr.Api.V5/` — sole REST surface (V3 wholesale-deleted Phase 15 Plan 15-06)
- **REST base**: [src/Mangarr.Http/REST/RestController.cs](./src/Mangarr.Http/REST/RestController.cs)
- **Auth**: [src/Mangarr.Http/Authentication/AuthenticationService.cs](./src/Mangarr.Http/Authentication/AuthenticationService.cs)
- **Error pipeline**: [src/Mangarr.Http/ErrorManagement/MangarrErrorPipeline.cs](./src/Mangarr.Http/ErrorManagement/MangarrErrorPipeline.cs)

### SignalR
- **Hub**: [src/NzbDrone.SignalR/MessageHub.cs](./src/NzbDrone.SignalR/MessageHub.cs) (class is `MessageHub`, not `SonarrHub`)
- **Broadcaster interface**: [src/NzbDrone.SignalR/IBroadcastSignalRMessage.cs](./src/NzbDrone.SignalR/IBroadcastSignalRMessage.cs)

### Frontend Entry Points
- **HTML entry**: `frontend/src/index.ts` → fetches `/initialize.json` → loads `bootstrap`
- **React entry**: [frontend/src/bootstrap.tsx](./frontend/src/bootstrap.tsx) → creates store + history → renders `<App />`
- **Root component**: [frontend/src/App/App.tsx](./frontend/src/App/App.tsx) → providers (DocumentTitle → QueryClient → Redux → Router → ApplyTheme → Page → Routes)
- **Routes**: [frontend/src/App/AppRoutes.tsx](./frontend/src/App/AppRoutes.tsx)

### Configuration
- **Build props**: `src/Directory.Build.props`
- **Package.json**: `package.json`
- **Webpack**: `frontend/build/webpack.config.js`
- **Global JSON (SDK pinning)**: `global.json`

## Frontend Architecture

### Three-store Hybrid State Model

| Store | Purpose | Persistence | Example |
|-------|---------|-------------|---------|
| **Redux** | App-wide settings, custom filters, commands, captcha | Some via custom middleware | `state.settings.ui` |
| **Zustand** | Per-feature view options (poster size, sort, filter) | localStorage via `persist` | `mangaOptionsStore` |
| **React Query** | Server state cache (API responses) | Memory only, configurable staleTime | `useApiQuery({ queryKey: ['/manga'] })` |

Most new feature work prefers **Zustand + React Query** over Redux. Redux remains because legacy settings/profiles flows are deeply integrated with it.

### Routing (from AppRoutes.tsx)

| Path | Component | Section |
|------|-----------|---------|
| `/` | MangaIndex | Manga (Home) |
| `/add/manga` | AddNewManga | Add |
| `/add/import` | ImportMangaPage | Add |
| `/manga/:titleSlug` | (manga details) | Manga detail |
| `/calendar` | CalendarPage | Calendar |
| `/manga/activity/history` | MangaHistory | Activity |
| `/manga/activity/queue` | MangaQueue | Activity |
| `/manga/activity/blocklist` | MangaBlocklist | Activity |
| `/manga/wanted/missing` | MangaMissing | Wanted |
| `/manga/wanted/cutoffunmet` | MangaCutoffUnmet | Wanted |
| `/settings` | Settings | Settings home |
| `/settings/mediamanagement` | MediaManagement | Settings |
| `/settings/profiles` | Profiles | Settings |
| `/settings/customformatprofiles` | (custom format profiles) | Settings |
| `/settings/customformats` | CustomFormatSettingsPage | Settings |
| `/settings/indexers` | IndexerSettings | Settings |
| `/settings/downloadclients` | DownloadClientSettings | Settings |
| `/settings/importlists` | ImportListSettings | Settings |
| `/settings/connect` | NotificationSettings | Settings |
| `/settings/metadata` | MetadataSettings | Settings |
| `/settings/metadatasource` | MetadataSourceSettings | Settings |
| `/settings/tags` | TagSettings | Settings |
| `/settings/general` | GeneralSettings | Settings |
| `/settings/ui` | UISettings | Settings |
| `/system/status` | Status | System |
| `/system/tasks` | Tasks | System |
| `/system/backup` | Backups | System |
| `/system/updates` | Updates | System |
| `/system/events` | LogsTable | System |
| `/system/logs/files` | Logs | System |
| `*` | NotFound | Catch-all |

### App Provider Tree

```
<DocumentTitle title={window.Mangarr.instanceName}>
  └─ <QueryClientProvider>           // TanStack React Query
     └─ <Provider store={store}>     // Redux
        └─ <ConnectedRouter history>
           ├─ <ApplyTheme>           // Theme application
           └─ <Page>                 // Sidebar + content area
              └─ <AppRoutes>
```

### Build Output

Webpack builds to `_output/UI/` (not `frontend/dist/`). The C# `Mangarr.Http.Frontend` mappers serve files from there. Asset paths use `window.Mangarr.urlBase` for reverse-proxy compatibility.

## Database

### Two databases (separate files):
1. **Main** (`mangarr.db`): All entity tables (Manga, Chapters, ChapterFiles, History, Indexers, etc.)
2. **Logs** (`logs.db`): Application log records

### Migration system:
- **Engine**: FluentMigrator
- **Path**: `src/NzbDrone.Core/Datastore/Migration/`
- **Naming**: Sequential `NNN_description.cs` (currently `001_mangarr_baseline.cs` through `017_…`; post-v1.0.0 policy appends a new sequential migration — the pre-v1 edit-baseline-in-place rule no longer applies)
- **Style**: Each file is a class extending `NzbDroneMigrationBase`, with `protected override void MainDbUpgrade()`
- Migrations run automatically on startup before service registration completes.

### Switching to Postgres
Set `Mangarr:Postgres:Host`/Port/User/Password in `config.xml`. Connection-string factory at `src/NzbDrone.Core/Datastore/ConnectionStringFactory.cs`. NOTE: a separate `postgres.runsettings` exists for testing.

## Build & Run

```bash
# Install dependencies
yarn install

# Build backend
dotnet build src/Mangarr.sln --configuration Debug

# Build frontend
yarn build

# Run application (full)
dotnet run --project src/NzbDrone.Console/Mangarr.Console.csproj

# Frontend watch mode (during UI dev)
yarn watch

# Run tests
export TEST_DIR="./_tests/net10.0"
bash scripts/test.sh Windows Unit Test
```

Application: **http://localhost:8989**

## Cross-References

| Component | Documentation |
|-----------|---------------|
| Root Project | [CLAUDE.md](./CLAUDE.md) |
| NzbDrone.Core | [src/NzbDrone.Core/CLAUDE.md](./src/NzbDrone.Core/CLAUDE.md) |
| Manga (domain) | [src/NzbDrone.Core/Manga/CLAUDE.md](./src/NzbDrone.Core/Manga/CLAUDE.md) |
| Parser | [src/NzbDrone.Core/Parser/CLAUDE.md](./src/NzbDrone.Core/Parser/CLAUDE.md) |
| DecisionEngine | [src/NzbDrone.Core/DecisionEngine/CLAUDE.md](./src/NzbDrone.Core/DecisionEngine/CLAUDE.md) |
| Indexers | [src/NzbDrone.Core/Indexers/CLAUDE.md](./src/NzbDrone.Core/Indexers/CLAUDE.md) |
| Download | [src/NzbDrone.Core/Download/CLAUDE.md](./src/NzbDrone.Core/Download/CLAUDE.md) |
| MediaFiles | [src/NzbDrone.Core/MediaFiles/CLAUDE.md](./src/NzbDrone.Core/MediaFiles/CLAUDE.md) |
| MetadataSource | [src/NzbDrone.Core/MetadataSource/CLAUDE.md](./src/NzbDrone.Core/MetadataSource/CLAUDE.md) |
| Datastore | [src/NzbDrone.Core/Datastore/CLAUDE.md](./src/NzbDrone.Core/Datastore/CLAUDE.md) |
| NzbDrone.Common | [src/NzbDrone.Common/CLAUDE.md](./src/NzbDrone.Common/CLAUDE.md) |
| Mangarr.Api.V5 | [src/Mangarr.Api.V5/CLAUDE.md](./src/Mangarr.Api.V5/CLAUDE.md) |
| Mangarr.Http | [src/Mangarr.Http/CLAUDE.md](./src/Mangarr.Http/CLAUDE.md) |
| NzbDrone.Host | [src/NzbDrone.Host/CLAUDE.md](./src/NzbDrone.Host/CLAUDE.md) |
| Frontend | [frontend/CLAUDE.md](./frontend/CLAUDE.md) |
| Frontend App | [frontend/src/App/CLAUDE.md](./frontend/src/App/CLAUDE.md) |
| Frontend Manga | [frontend/src/Manga/CLAUDE.md](./frontend/src/Manga/CLAUDE.md) |
| Frontend Settings | [frontend/src/Settings/CLAUDE.md](./frontend/src/Settings/CLAUDE.md) |
