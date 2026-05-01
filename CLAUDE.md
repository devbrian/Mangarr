# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/claude-code) when working with this codebase.

> **HIGH PRIORITY: DOCUMENTATION MAINTENANCE**
>
> As you work on this project, you MUST keep documentation up to date:
>
> 1. **Update existing CLAUDE.md files** when you modify code in that directory
> 2. **Create new CLAUDE.md files** when adding new directories or major features
> 3. **Update PROJECT_CONTEXT.md** when making architectural changes
> 4. **Update cross-references** when renaming or moving files
> 5. **Document Sonarr→Mangarr changes** as migrations are completed
>
> Accurate documentation is critical for maintaining context across sessions.

---

## Project Overview

**Mangarr** is a manga/manhwa/manhua library manager and downloader. It monitors manga reader and aggregator websites for new chapters of your favorite titles, automatically downloads, sorts, and organizes them. It can also be configured to automatically upgrade quality when better scans become available.

This project is a **fork/migration of [Sonarr](https://github.com/Sonarr/Sonarr)**, adapting its mature TV-show management infrastructure to manga management. Most file/class names still use Sonarr/TV terminology — the migration is **in progress** and proceeds incrementally.

**Branch model**: `Mangarr-v0` is the long-lived integration branch — **PRs should target `Mangarr-v0`**. The `v5-develop` branch tracks upstream Sonarr v5 and is only used when pulling in upstream changes.

## Migration: Sonarr → Mangarr

### Conceptual Mapping

| Sonarr Concept | Mangarr Concept | Notes |
|----------------|-----------------|-------|
| Series | Manga / Manhwa / Manhua | Core entity (a "title") |
| Season | Volume *(optional)* | Many manga series do not use volumes |
| Episode | Chapter | Individual release |
| EpisodeFile | ChapterFile | Physical artifact (CBZ/CBR/folder of images) |
| TVDB / TheTVDB | MangaDex / AniList / MyAnimeList | Metadata sources |
| Indexers (Usenet/Torrent) | Manga aggregator sites | Web scrapers |
| Download Clients | Chapter downloaders | Image scrapers |
| Quality (720p/1080p) | Scan quality (raw/translated/official, DPI tiers) | Different quality model |
| Air Date | Release Date | No "airing" concept |

The Sonarr `Series` model has **already been extended** with `MalIds` and `AniListIds` properties — see [src/NzbDrone.Core/Tv/Series.cs](./src/NzbDrone.Core/Tv/Series.cs) — indicating partial migration work has begun.

### Migration Priority Map

| Layer | Priority | Status |
|-------|----------|--------|
| **Domain models** (Tv/Series, Episode, Season) | CRITICAL | Series partially extended (MAL/AniList IDs) |
| **Parser regex patterns** | CRITICAL | TV-only |
| **MetadataSource** (TVDB → MangaDex/AniList) | CRITICAL | TV-only (SkyHook = TVDB) |
| **Quality definitions** | CRITICAL | TV resolutions only |
| **Indexers** (need manga site scrapers) | HIGH | Anime indexers exist (Nyaa) |
| **MediaFiles** (CBZ/CBR vs video) | HIGH | TV file logic |
| **DecisionEngine specifications** | HIGH | TV-aware specs |
| **Frontend Series/Episode pages** | HIGH | Renamed dirs needed |
| **API V5 controllers** | HIGH | Names + endpoints |
| **CustomFormats / Profiles** | MEDIUM | Pattern reusable |
| **Notifications, Authentication, Tags, Backup, Update, Health** | LOW / NONE | Reusable as-is |

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Backend Runtime | .NET 10.0 (C#) |
| Frontend | React 18.3 + TypeScript 5.7 |
| Database | SQLite (default) / PostgreSQL |
| ORM | Dapper (micro-ORM) over FluentMigrator schema migrations |
| State Management | Redux 4.2 + Zustand 5 + TanStack React Query 5.61 |
| Real-time | SignalR 10 |
| Build | MSBuild (backend), Webpack 5 + Babel (frontend), Yarn 4 |
| Testing | NUnit |
| DI Container | DryIoc |
| Logging | NLog |
| Validation | FluentValidation |

## Prerequisites

- .NET SDK 10.0.203 (`winget install Microsoft.DotNet.SDK.10 --source winget`)
- Node.js 20.x or higher
- Yarn (enable with `corepack enable`)

## Running Locally

```bash
# 1. Install frontend deps
yarn install

# 2. Build backend & frontend
dotnet build src/Sonarr.sln --configuration Debug
yarn build

# 3. Run
dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj
```

App listens at **http://localhost:8989**.

## Project Structure (Top-Level)

```
Mangarr/
├── src/                              # Backend C# source
│   ├── NzbDrone.Common/              # Shared utilities (179 files, no NzbDrone deps)
│   ├── NzbDrone.Core/                # Business logic — LARGEST project
│   ├── NzbDrone.Host/                # ASP.NET Core host + bootstrap
│   ├── NzbDrone.Console/             # Console executable entry point
│   ├── NzbDrone.SignalR/             # Real-time push to UI
│   ├── NzbDrone.Update/              # Self-update mechanism
│   ├── NzbDrone.Mono/                # Linux/Mac platform code
│   ├── NzbDrone.Windows/             # Windows platform code
│   ├── Sonarr.Http/                  # REST base / middleware / auth (70 files)
│   ├── Sonarr.Api.V5/                # REST API v5 (149 files, 44 controllers)
│   ├── Sonarr.Api.V3/                # REST API v3 (legacy, 156 files)
│   ├── Sonarr.RuntimePatches/        # Runtime monkey-patches
│   ├── ServiceHelpers/               # Service install helpers
│   ├── Libraries/                    # Vendored binaries
│   ├── *.Test/ projects              # NUnit test projects
│   └── Sonarr.sln                    # Solution file
├── frontend/                         # React + TypeScript UI
│   ├── src/                          # 39 top-level dirs (see frontend/CLAUDE.md)
│   └── build/webpack.config.js       # Webpack config
├── _output/                          # Build output (UI assets bundled in)
├── _tests/                           # Test output
├── scripts/                          # Build/test bash scripts
├── distribution/                     # Packaging
├── docker/                           # Docker images
├── PROJECT_CONTEXT.md                # Architecture deep-dive
└── CLAUDE.md                         # This file
```

## Quick Reference: Where Does X Live?

| If you need to change... | Look in... |
|--------------------------|-----------|
| Series/Manga domain logic | [src/NzbDrone.Core/Tv/](./src/NzbDrone.Core/Tv/) |
| Episode/Chapter logic | [src/NzbDrone.Core/Tv/](./src/NzbDrone.Core/Tv/) (Episode.cs) |
| File handling (CBZ/CBR) | [src/NzbDrone.Core/MediaFiles/](./src/NzbDrone.Core/MediaFiles/) |
| Title/release parsing regex | [src/NzbDrone.Core/Parser/](./src/NzbDrone.Core/Parser/) |
| Search filters / specs | [src/NzbDrone.Core/DecisionEngine/Specifications/](./src/NzbDrone.Core/DecisionEngine/Specifications/) |
| Indexer integrations | [src/NzbDrone.Core/Indexers/](./src/NzbDrone.Core/Indexers/) |
| Download client integrations | [src/NzbDrone.Core/Download/Clients/](./src/NzbDrone.Core/Download/Clients/) |
| Notification providers | [src/NzbDrone.Core/Notifications/](./src/NzbDrone.Core/Notifications/) |
| Metadata source (TVDB/AniList) | [src/NzbDrone.Core/MetadataSource/](./src/NzbDrone.Core/MetadataSource/) |
| Database schema | [src/NzbDrone.Core/Datastore/Migration/](./src/NzbDrone.Core/Datastore/Migration/) (224 migrations) |
| API endpoints (V5) | [src/Sonarr.Api.V5/](./src/Sonarr.Api.V5/) |
| Auth / middleware / REST base | [src/Sonarr.Http/](./src/Sonarr.Http/) |
| App startup / DI registration | [src/NzbDrone.Host/Startup.cs](./src/NzbDrone.Host/Startup.cs), [src/NzbDrone.Host/Bootstrap.cs](./src/NzbDrone.Host/Bootstrap.cs) |
| React routes | [frontend/src/App/AppRoutes.tsx](./frontend/src/App/AppRoutes.tsx) |
| Series list page (UI) | [frontend/src/Series/Index/](./frontend/src/Series/Index/) |
| Series detail page (UI) | [frontend/src/Series/Details/](./frontend/src/Series/Details/) |
| Add Series flow (UI) | [frontend/src/AddSeries/](./frontend/src/AddSeries/) |
| Settings pages (UI) | [frontend/src/Settings/](./frontend/src/Settings/) |
| Calendar (UI) | [frontend/src/Calendar/](./frontend/src/Calendar/) |
| Queue / History / Blocklist UI | [frontend/src/Activity/](./frontend/src/Activity/) |
| Wanted (Missing/CutoffUnmet) UI | [frontend/src/Wanted/](./frontend/src/Wanted/) |
| Manual search / import UI | [frontend/src/InteractiveSearch/](./frontend/src/InteractiveSearch/), [frontend/src/InteractiveImport/](./frontend/src/InteractiveImport/) |
| Shared UI components | [frontend/src/Components/](./frontend/src/Components/) |
| Redux store | [frontend/src/Store/](./frontend/src/Store/) |
| Custom hooks (useApiQuery, etc.) | [frontend/src/Helpers/](./frontend/src/Helpers/) |
| TypeScript types (API DTOs) | [frontend/src/typings/](./frontend/src/typings/) |

## Running Tests

Sonarr/Mangarr uses NUnit. The script at `scripts/test.sh` requires three params:

```bash
export TEST_DIR="./_tests/net10.0"
bash scripts/test.sh Windows Unit Test         # Unit tests
bash scripts/test.sh Windows Integration Test  # Integration
bash scripts/test.sh Windows Unit Coverage     # With coverage
```

Results: `TestResult.xml` (project root).

Tests projects: `NzbDrone.Core.Test`, `NzbDrone.Common.Test`, `NzbDrone.Host.Test`, `NzbDrone.Api.Test`, `NzbDrone.Integration.Test`, `NzbDrone.Automation.Test`, etc.

## Common Commands

```bash
yarn clean && yarn build                                    # Clean + dev build
dotnet build src/Sonarr.sln --configuration Release         # Release build
yarn build --env production                                 # Production frontend bundle
yarn lint && yarn lint-fix                                  # Lint
yarn stylelint                                              # CSS lint
yarn watch                                                  # Webpack watch mode
```

## Development Notes

- **Solution file**: `src/Sonarr.sln`
- **Database migrations**: Auto-applied on startup. Add new migration in `src/NzbDrone.Core/Datastore/Migration/`. Migrations are sequential (`000_…` → `223_…` currently).
- **Default data dir**: `C:\ProgramData\Sonarr` (Win) / `~/.config/Sonarr` (Linux/Mac). Logs in `<data>/logs/`.
- **Default port**: 8989 (override with `--port=NNNN`).
- **API key**: Auto-generated on first run; check `<data>/config.xml` or General settings.
- **API auth**: `X-Api-Key` header OR `?apikey=…` query OR cookie auth for UI.
- **DI Container**: DryIoc, services auto-registered by convention via assembly scanning.
- **Auto-discovery**: Indexers, DownloadClients, Notifications, ImportLists, MetadataSources, Specifications, HealthChecks are all discovered by reflection through the **ThingiProvider** pattern (`src/NzbDrone.Core/ThingiProvider/`).

---

## Documentation Maintenance Guidelines

### When to Update Documentation

| Action | Documentation Update Required |
|--------|------------------------------|
| Modify files in a directory | Update that directory's `CLAUDE.md` |
| Add new directory/feature | Create new `CLAUDE.md` in that directory |
| Rename/move files | Update cross-references in all affected docs |
| Change architecture | Update `PROJECT_CONTEXT.md` |
| Complete Sonarr→Mangarr migration | Mark as completed in relevant docs |
| Add new API endpoints | Update `src/Sonarr.Api.V5/CLAUDE.md` |
| Add new domain models | Update `src/NzbDrone.Core/CLAUDE.md` (and create file-specific CLAUDE.md if major) |
| Add new UI components | Update `frontend/src/Components/CLAUDE.md` |
| Add a new Specification | Update `src/NzbDrone.Core/DecisionEngine/CLAUDE.md` |
| Add a new Indexer / DownloadClient / Notification | Update the respective `CLAUDE.md` and ensure ThingiProvider registration |

### Documentation Template for New Directories

```markdown
# [Directory Name]

## Purpose
[Brief description of what this directory contains]

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\[path]`

## Key Files
| File | Purpose |
|------|---------|
| `file.ts` | Description |

## Patterns / Conventions
[Code examples if applicable]

## Manga Adaptation Notes
[What needs to change for the Sonarr→Mangarr migration]

## Cross-References
- [Related Doc](./path/to/CLAUDE.md)
```

---

## Documentation Index

### Architecture
- [PROJECT_CONTEXT.md](./PROJECT_CONTEXT.md) — Overall architecture and data flow

### Backend Components
| Component | Documentation | Purpose |
|-----------|---------------|---------|
| NzbDrone.Core (overview) | [src/NzbDrone.Core/CLAUDE.md](./src/NzbDrone.Core/CLAUDE.md) | Core business logic |
| `Tv/` (domain) | [src/NzbDrone.Core/Tv/CLAUDE.md](./src/NzbDrone.Core/Tv/CLAUDE.md) | Series/Episode/Season models |
| `Parser/` | [src/NzbDrone.Core/Parser/CLAUDE.md](./src/NzbDrone.Core/Parser/CLAUDE.md) | Title/release parsing |
| `DecisionEngine/` | [src/NzbDrone.Core/DecisionEngine/CLAUDE.md](./src/NzbDrone.Core/DecisionEngine/CLAUDE.md) | Specification rules |
| `Indexers/` | [src/NzbDrone.Core/Indexers/CLAUDE.md](./src/NzbDrone.Core/Indexers/CLAUDE.md) | Indexer integrations |
| `Download/` | [src/NzbDrone.Core/Download/CLAUDE.md](./src/NzbDrone.Core/Download/CLAUDE.md) | Download clients & pipeline |
| `MediaFiles/` | [src/NzbDrone.Core/MediaFiles/CLAUDE.md](./src/NzbDrone.Core/MediaFiles/CLAUDE.md) | File import / scan / move |
| `MetadataSource/` | [src/NzbDrone.Core/MetadataSource/CLAUDE.md](./src/NzbDrone.Core/MetadataSource/CLAUDE.md) | TVDB → manga sources |
| `Datastore/` | [src/NzbDrone.Core/Datastore/CLAUDE.md](./src/NzbDrone.Core/Datastore/CLAUDE.md) | DB / migrations / repo |
| `Notifications/` | [src/NzbDrone.Core/Notifications/CLAUDE.md](./src/NzbDrone.Core/Notifications/CLAUDE.md) | Notification providers |
| `CustomFormats/` | [src/NzbDrone.Core/CustomFormats/CLAUDE.md](./src/NzbDrone.Core/CustomFormats/CLAUDE.md) | Custom format rules |
| `Profiles/` | [src/NzbDrone.Core/Profiles/CLAUDE.md](./src/NzbDrone.Core/Profiles/CLAUDE.md) | Quality / Delay / Release profiles |
| `Messaging/` | [src/NzbDrone.Core/Messaging/CLAUDE.md](./src/NzbDrone.Core/Messaging/CLAUDE.md) | Events / Commands |
| `ImportLists/` | [src/NzbDrone.Core/ImportLists/CLAUDE.md](./src/NzbDrone.Core/ImportLists/CLAUDE.md) | External list ingestion |
| NzbDrone.Common | [src/NzbDrone.Common/CLAUDE.md](./src/NzbDrone.Common/CLAUDE.md) | Shared utilities |
| Sonarr.Api.V5 | [src/Sonarr.Api.V5/CLAUDE.md](./src/Sonarr.Api.V5/CLAUDE.md) | REST API (current) |
| Sonarr.Api.V3 | [src/Sonarr.Api.V3/CLAUDE.md](./src/Sonarr.Api.V3/CLAUDE.md) | REST API (legacy) |
| Sonarr.Http | [src/Sonarr.Http/CLAUDE.md](./src/Sonarr.Http/CLAUDE.md) | HTTP infrastructure |
| NzbDrone.Host | [src/NzbDrone.Host/CLAUDE.md](./src/NzbDrone.Host/CLAUDE.md) | App host / DI / startup |
| NzbDrone.Console | [src/NzbDrone.Console/CLAUDE.md](./src/NzbDrone.Console/CLAUDE.md) | Console entry point |
| NzbDrone.SignalR | [src/NzbDrone.SignalR/CLAUDE.md](./src/NzbDrone.SignalR/CLAUDE.md) | Real-time hub |

### Frontend Components
| Component | Documentation |
|-----------|---------------|
| Frontend Root | [frontend/CLAUDE.md](./frontend/CLAUDE.md) |
| `App/` (root + routing) | [frontend/src/App/CLAUDE.md](./frontend/src/App/CLAUDE.md) |
| `Series/` (→ Manga) | [frontend/src/Series/CLAUDE.md](./frontend/src/Series/CLAUDE.md) |
| `Episode/` (→ Chapter) | [frontend/src/Episode/CLAUDE.md](./frontend/src/Episode/CLAUDE.md) |
| `Components/` (shared UI) | [frontend/src/Components/CLAUDE.md](./frontend/src/Components/CLAUDE.md) |
| `Store/` (Redux) | [frontend/src/Store/CLAUDE.md](./frontend/src/Store/CLAUDE.md) |
| `Helpers/` (hooks) | [frontend/src/Helpers/CLAUDE.md](./frontend/src/Helpers/CLAUDE.md) |
| `Settings/` | [frontend/src/Settings/CLAUDE.md](./frontend/src/Settings/CLAUDE.md) |
| `AddSeries/` (→ AddManga) | [frontend/src/AddSeries/CLAUDE.md](./frontend/src/AddSeries/CLAUDE.md) |
| `Activity/` (Queue/History/Blocklist) | [frontend/src/Activity/CLAUDE.md](./frontend/src/Activity/CLAUDE.md) |
| `Calendar/` | [frontend/src/Calendar/CLAUDE.md](./frontend/src/Calendar/CLAUDE.md) |
| `Wanted/` (Missing/Cutoff) | [frontend/src/Wanted/CLAUDE.md](./frontend/src/Wanted/CLAUDE.md) |
| `InteractiveSearch/` | [frontend/src/InteractiveSearch/CLAUDE.md](./frontend/src/InteractiveSearch/CLAUDE.md) |
| `InteractiveImport/` | [frontend/src/InteractiveImport/CLAUDE.md](./frontend/src/InteractiveImport/CLAUDE.md) |
| `System/` | [frontend/src/System/CLAUDE.md](./frontend/src/System/CLAUDE.md) |
| `Utilities/` | [frontend/src/Utilities/CLAUDE.md](./frontend/src/Utilities/CLAUDE.md) |
| `typings/` | [frontend/src/typings/CLAUDE.md](./frontend/src/typings/CLAUDE.md) |

---

## Help & Feedback

- `/help` — Get help with using Claude Code
- Report issues at https://github.com/anthropics/claude-code/issues

<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**IMPORTANT: This project has a knowledge graph. ALWAYS use the
code-review-graph MCP tools BEFORE using Grep/Glob/Read to explore
the codebase.** The graph is faster, cheaper (fewer tokens), and gives
you structural context (callers, dependents, test coverage) that file
scanning cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes` or `query_graph` instead of Grep
- **Understanding impact**: `get_impact_radius` instead of manually tracing imports
- **Code review**: `detect_changes` + `get_review_context` instead of reading entire files
- **Finding relationships**: `query_graph` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview` + `list_communities`

Fall back to Grep/Glob/Read **only** when the graph doesn't cover what you need.

### Key Tools

| Tool | Use when |
|------|----------|
| `detect_changes` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context` | Need source snippets for review — token-efficient |
| `get_impact_radius` | Understanding blast radius of a change |
| `get_affected_flows` | Finding which execution paths are impacted |
| `query_graph` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes` | Finding functions/classes by name or keyword |
| `get_architecture_overview` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes` for code review.
3. Use `get_affected_flows` to understand impact.
4. Use `query_graph` pattern="tests_for" to check coverage.

---

## Project Planning ([.planning/](./.planning/))

This project uses **GSD** (`/gsd-*` commands) for structured planning. The single source of truth for "what we're building and why" lives in `.planning/`. Read these before answering questions about scope, decisions, or roadmap:

| File | What it is |
|------|------------|
| [.planning/PROJECT.md](./.planning/PROJECT.md) | Vision, core value, v1 scope, out-of-scope, key decisions, design philosophy |
| [.planning/REQUIREMENTS.md](./.planning/REQUIREMENTS.md) | 71 v1 requirements across 19 categories with traceability to phases |
| [.planning/ROADMAP.md](./.planning/ROADMAP.md) | 9-phase v1 plan (Phase 0 decisions + 8 implementation phases); leaf-first, rename-last |
| [.planning/STATE.md](./.planning/STATE.md) | Current phase + plan, recent decisions, blockers, session continuity |
| [.planning/codebase/](./.planning/codebase/) | 7-doc map of inherited Sonarr fork (STACK, ARCHITECTURE, STRUCTURE, CONVENTIONS, TESTING, INTEGRATIONS, CONCERNS) |
| [.planning/research/](./.planning/research/) | Web-verified domain research: STACK, FEATURES, ARCHITECTURE, PITFALLS, SUMMARY |
| [.planning/config.json](./.planning/config.json) | GSD workflow preferences (mode, granularity, model profile, agent toggles) |

**Design philosophy** (locked in PROJECT.md): *Preserve Sonarr's shape wherever it works; diverge only where the manga domain forces us.* This drives every gray-area call.

**Per-phase workflow:** `/gsd-discuss-phase N` → `/gsd-plan-phase N` → `/gsd-execute-phase N` → `/gsd-verify-work N`. Or `/gsd-progress` for the unified situational command.
