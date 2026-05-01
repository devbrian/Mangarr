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
>
> Accurate documentation is critical for maintaining context across sessions.

## Overview

**Mangarr** is a manga/manhwa/manhua library manager forked from Sonarr. This document provides architectural context for the entire codebase.

## Technology Stack

| Layer | Technology |
|-------|------------|
| Backend Runtime | .NET 10.0 (C#) |
| Frontend | React 18.3 + TypeScript |
| Database | SQLite (default) / PostgreSQL |
| ORM | Dapper (micro-ORM) |
| State Management | Redux + Zustand + React Query |
| Real-time | SignalR |
| Build | MSBuild (backend), Webpack 5 (frontend), Yarn |
| Testing | NUnit |

## Directory Structure

```
C:\Users\jones\Desktop\Mangarr\Mangarr\
├── src/                          # Backend C# source
│   ├── NzbDrone.Common/          # Shared utilities
│   ├── NzbDrone.Core/            # Business logic (largest)
│   ├── NzbDrone.Host/            # Application host
│   ├── NzbDrone.Console/         # Console entry point
│   ├── Sonarr.Http/              # HTTP/REST infrastructure
│   ├── Sonarr.Api.V5/            # API v5 controllers
│   ├── Sonarr.Api.V3/            # API v3 (legacy)
│   ├── NzbDrone.SignalR/         # Real-time messaging
│   ├── NzbDrone.Windows/         # Windows-specific code
│   ├── NzbDrone.Mono/            # Mono/Linux-specific code
│   ├── NzbDrone.Update/          # Self-update mechanism
│   └── *Test projects*/          # Unit/integration tests
├── frontend/                     # React frontend
│   ├── src/                      # Source code
│   │   ├── App/                  # App root & routing
│   │   ├── Series/               # Series feature module
│   │   ├── Episode/              # Episode components
│   │   ├── Components/           # Shared UI components
│   │   ├── Store/                # Redux store
│   │   └── Helpers/              # Hooks & utilities
│   └── build/                    # Webpack config
├── _output/                      # Build output
├── _tests/                       # Test output
└── scripts/                      # Build/test scripts
```

## Architecture Layers

```
┌─────────────────────────────────────────────────────────┐
│                    Frontend (React)                      │
│  Series/Episode UI, State Management, API Integration   │
└─────────────────────────┬───────────────────────────────┘
                          │ HTTP/SignalR
┌─────────────────────────┴───────────────────────────────┐
│                 Sonarr.Api.V5 / V3                       │
│           REST Controllers, Request Handling            │
└─────────────────────────┬───────────────────────────────┘
                          │
┌─────────────────────────┴───────────────────────────────┐
│                    Sonarr.Http                           │
│      Middleware, Authentication, Error Handling         │
└─────────────────────────┬───────────────────────────────┘
                          │
┌─────────────────────────┴───────────────────────────────┐
│                   NzbDrone.Host                          │
│          Application Bootstrap, Server Lifecycle        │
└─────────────────────────┬───────────────────────────────┘
                          │
┌─────────────────────────┴───────────────────────────────┐
│                   NzbDrone.Core                          │
│     Business Logic, Domain Models, Services, Events     │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌─────────────┐  │
│  │   Tv/   │ │ Parser/ │ │ Indexers │ │DecisionEngine│ │
│  │ Series  │ │ Release │ │ Search   │ │Specifications│  │
│  │ Episode │ │ Parsing │ │ Fetch    │ │   Logic     │  │
│  └─────────┘ └─────────┘ └──────────┘ └─────────────┘  │
│  ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌─────────────┐  │
│  │Download │ │MediaFile│ │Datastore │ │  Messaging  │  │
│  │ Clients │ │ Import  │ │   ORM    │ │   Events    │  │
│  └─────────┘ └─────────┘ └──────────┘ └─────────────┘  │
└─────────────────────────┬───────────────────────────────┘
                          │
┌─────────────────────────┴───────────────────────────────┐
│                  NzbDrone.Common                         │
│        Utilities, Disk I/O, HTTP Client, Logging        │
└─────────────────────────────────────────────────────────┘
```

## Core Data Flow

### Search & Download Pipeline

```
1. Indexer RSS/Search
       ↓
2. ReleaseInfo collection
       ↓
3. Parser.ParseTitle() → ParsedEpisodeInfo
       ↓
4. ParsingService.Map() → RemoteEpisode (links to Series/Episodes)
       ↓
5. DecisionEngine runs Specifications
   ├─ Quality checks
   ├─ Upgrade logic
   ├─ Blocklist check
   └─ ~30 more specs
       ↓
6. DownloadDecision (Approved/Rejected)
       ↓
7. DownloadService → Download Client
       ↓
8. CompletedDownloadService processes completion
       ↓
9. ImportService organizes files
       ↓
10. Database updated, Events published
```

## Key Architectural Patterns

### 1. Event-Driven Architecture
- `IEventAggregator` publishes events
- `IHandle<TEvent>` subscribers react asynchronously
- Decouples services for extensibility

### 2. Specification Pattern (Decision Engine)
- Each rule implements `IDownloadDecisionEngineSpecification`
- Returns Approve/Reject with reason
- Composable and independently testable

### 3. Provider/Plugin System
- Base: `IProvider` interface (ThingiProvider)
- Used for: Indexers, DownloadClients, Notifications
- Dynamic discovery via reflection
- Settings serialized as JSON

### 4. Repository Pattern
- `BasicRepository<T>` generic base
- Dapper for SQL execution
- Lazy loading via `LazyLoaded<T>`

### 5. Command Pattern
- Commands encapsulate user actions
- Executed asynchronously via message queue
- Tracked for progress/completion

## Domain Model Mapping (Sonarr → Mangarr)

| Sonarr | Mangarr | Location |
|--------|---------|----------|
| Series | Manga | `src/NzbDrone.Core/Tv/Series.cs` |
| Season | Volume | `src/NzbDrone.Core/Tv/Season.cs` |
| Episode | Chapter | `src/NzbDrone.Core/Tv/Episode.cs` |
| EpisodeFile | ChapterFile | `src/NzbDrone.Core/MediaFiles/EpisodeFile.cs` |
| TVDB | MangaDex/AniList | `src/NzbDrone.Core/MetadataSource/` |

## Key Files Reference

### Backend Entry Points
- **Solution**: `src/Sonarr.sln`
- **Console App**: `src/NzbDrone.Console/Program.cs`
- **Host Bootstrap**: `src/NzbDrone.Host/Bootstrap.cs`

### Core Domain
- **Series Model**: `src/NzbDrone.Core/Tv/Series.cs`
- **Episode Model**: `src/NzbDrone.Core/Tv/Episode.cs`
- **Release Parser**: `src/NzbDrone.Core/Parser/Parser.cs`
- **Decision Engine**: `src/NzbDrone.Core/DecisionEngine/DownloadDecisionMaker.cs`

### API Layer
- **V5 Controllers**: `src/Sonarr.Api.V5/`
- **HTTP Base**: `src/Sonarr.Http/REST/RestController.cs`

### Frontend Entry Points
- **App Root**: `frontend/src/App/App.tsx`
- **Routes**: `frontend/src/App/AppRoutes.tsx`
- **Series Feature**: `frontend/src/Series/`

### Configuration
- **Build Props**: `src/Directory.Build.props`
- **Package.json**: `package.json`
- **Webpack**: `frontend/build/webpack.config.js`

## Cross-References

| Component | Documentation |
|-----------|---------------|
| Root Project | [CLAUDE.md](./CLAUDE.md) |
| NzbDrone.Core | [src/NzbDrone.Core/CLAUDE.md](./src/NzbDrone.Core/CLAUDE.md) |
| NzbDrone.Common | [src/NzbDrone.Common/CLAUDE.md](./src/NzbDrone.Common/CLAUDE.md) |
| Sonarr.Api.V5 | [src/Sonarr.Api.V5/CLAUDE.md](./src/Sonarr.Api.V5/CLAUDE.md) |
| Sonarr.Http | [src/Sonarr.Http/CLAUDE.md](./src/Sonarr.Http/CLAUDE.md) |
| NzbDrone.Host | [src/NzbDrone.Host/CLAUDE.md](./src/NzbDrone.Host/CLAUDE.md) |
| Frontend | [frontend/CLAUDE.md](./frontend/CLAUDE.md) |
| Frontend Series | [frontend/src/Series/CLAUDE.md](./frontend/src/Series/CLAUDE.md) |

## Build & Run

```bash
# Install dependencies
yarn install

# Build backend
dotnet build src/Sonarr.sln --configuration Debug

# Build frontend
yarn build

# Run application
dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj

# Run tests
export TEST_DIR="./_tests/net10.0"
bash scripts/test.sh Windows Unit Test
```

Application runs at: **http://localhost:8989**
