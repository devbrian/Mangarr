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
> 5. **Document Sonarr → Mangarr divergences** in `DIVERGENCE.md` (Phase 15/16.1/17/17.3 close-out precedent)
>
> Accurate documentation is critical for maintaining context across sessions.

---

## Project Overview

**Mangarr** is a manga/manhwa/manhua library manager and downloader. It monitors manga reader and aggregator websites for new chapters of your favorite titles, automatically downloads, sorts, and organizes them. It can also be configured to automatically upgrade quality when better scans become available.

This project is a **fork/migration of [Sonarr](https://github.com/Sonarr/Sonarr)**, adapting its mature TV-show management infrastructure to manga management. Phase 15 (closed 2026-05-08) crossed the hard-fork threshold — canonical assembly names `Mangarr.*.dll`, default data dir `~/.config/Mangarr` / `C:\ProgramData\Mangarr`, canonical DB `mangarr.db`, canonical solution `src/Mangarr.sln`. The `NzbDrone.*` source-tree prefix is preserved per Phase 15 D-06 as a fork-heritage breadcrumb. Phase 16.1 — Revert ChapterRelease + Adopt Sonarr-Canonical Translation Pattern — closed 2026-05-10 (see `.planning/phases/16.1-revert-chapterrelease-adopt-sonarr-canonical-translation-pat/16.1-SUMMARY.md`). **Phase 17.3 — Domain Rename Residue Sweep** closed 2026-05-12; the 5 frontend stub directories (`Series/`, `Episode/`, `EpisodeFile/`, `Season/`, `Utilities/Series/`) were atomically deleted in Plan 17.3-13, TV-shape carry-over fields on `Manga.ts` were stripped in Plan 17.3-12 (D-13), the `SeasonPackUpgrade` vertical was deleted in Plan 17.3-05 (D-06), `SeriesNotFoundException.cs` was deleted in Plan 17.3-03 (D-05), 8 frontend Components/Form/* TV-named files were renamed in Plan 17.3-04 (D-07), and ~150 i18n keys were swept (Bucket A renames + Bucket B value rewrites + Bucket C PRESERVE catalog). Milestone status: **v1.0 shipped (Phase 21), v1.1 shipped (through Phase 28), v1.2 shipped (through Phase 35), and v1.3 is closing out at Phase 39 — Retire In-Process Codepath**. Phase 39 retired the in-process download/indexer machinery (in-process image downloader + MangaDex/Comix site scrapers + `IHttpAggregator` page-fetch coupling + the Cloudflare-clearance cascade + `Microsoft.Playwright` from `Mangarr.Core`): **Mangarr now ships zero embedded browser and drives only the external manga gateway** (`GatewayIndexer` + `GatewayDownloadClient`). See `.planning/STATE.md` and `.planning/ROADMAP.md` for the live phase/plan position.

**Branch model**: `Mangarr-v0` is the long-lived integration branch — **PRs should target `Mangarr-v0`**. The `v5-develop` branch tracks upstream Sonarr v5 and is only used when pulling in upstream changes.

## Migration: Sonarr → Mangarr

### Conceptual Mapping

| Sonarr Concept | Mangarr Concept | Notes |
|----------------|-----------------|-------|
| Series | Manga / Manhwa / Manhua | Core entity (a "title") |
| Season | (no peer; `volumeNumber` is display-only on `Chapter` per PROJECT.md Out-of-Scope) | Most manga (esp. manhwa) have no volumes; flat chapter list per PROJECT.md |
| Episode | Chapter | Individual release |
| EpisodeFile | ChapterFile | Physical artifact (CBZ/CBR/folder of images) |
| TVDB / TheTVDB | MangaDex / AniList / MyAnimeList | Metadata sources |
| Indexers (Usenet/Torrent) | External manga gateway (`GatewayIndexer`) | Sole `IIndexer` since Phase 39 — in-process site scrapers retired; the gateway runs the embedded browser, Mangarr ships none |
| Download Clients | External gateway download client (`GatewayDownloadClient`) | Sole download client since Phase 39 — the in-process image scraper was retired; the gateway delivers finished CBZs |
| Quality (720p/1080p) | Translation language (ordinal `TranslationProfile`) + Custom Formats | No resolution model in manga |
| Air Date | Release Date | No "airing" concept |

### Migration Priority Map

| Layer | Priority | Status |
|-------|----------|--------|
| **Domain models** (Manga/Chapter; no Season) | CRITICAL | Phase 15 Plan 15-03 deleted `Tv/`; `Manga/` + `Chapter` peers shipped. Phase 17.3 Plan 17.3-12 (D-13) trimmed Sonarr-shape carry-over fields. |
| **Parser regex patterns** | CRITICAL | Phase 4 + Phase 6 migrated `Parser.cs` to manga release naming |
| **MetadataSource** (MangaDex / AniList / MAL) | CRITICAL | Phase 2 shipped `MangaDexMetadataSource`; SkyHook deleted in Phase 15 |
| **Quality definitions** | CRITICAL | Phase 5 D-04 dropped TV-quality model; `TranslationProfile` + Custom Formats are the manga peer |
| **Indexers** (manga aggregator sites) | HIGH | In-process `MangaDexIndexer` + `ComixIndexer` (Phase 3) RETIRED in Phase 39 (Plan 39-03); `GatewayIndexer` (Phase 37) is now the sole `IIndexer` — Mangarr drives the external manga gateway only and ships zero embedded browser. |
| **Download client** (image fetch → CBZ) | HIGH | In-process `InProcessImageDownloadClient` + archiver vertical (Phase 4/6) RETIRED in Phase 39 (Plans 39-01/39-02); the external `GatewayDownloadClient` (Phase 38) is now the sole download client and delivers finished CBZs. No fresh-DB auto-seed (Sonarr-canonical empty download-client list). |
| **MediaFiles** (CBZ/CBR vs video) | HIGH | Phase 6 renamed `EpisodeFile.cs` → `ChapterFile.cs`; manga import pipeline at `MediaFiles/MangaImport/` |
| **DecisionEngine specifications** | HIGH | Phase 4/5 fork: TV-aware top-level `DecisionEngine/Specifications/` removed; manga decision specs at `DecisionEngine/Manga/Specifications/` (engine at `DecisionEngine/Manga/`) |
| **Frontend Manga/Chapter pages** | HIGH | Phase 7 shipped `Manga/` + `Chapter/`; Phase 15 Plan 15-12 + Phase 17.3 Plan 17.3-13 deleted Series/Episode/EpisodeFile/Season stub-dirs |
| **API V5 controllers** | HIGH | Phase 2/6/12 fork: `MangaController`, `ChapterController`, `ChapterFileController`, `MangaQueueController`, `MangaHistoryController`, `MangaBlocklistController`, `MangaMissingController`, `MangaCutoffController` shipped |
| **CustomFormats / Profiles** | MEDIUM | Phase 5 shipped `CustomFormatProfile` + `TranslationProfile` |
| **Notifications, Authentication, Tags, Backup, Update, Health** | LOW / NONE | Reusable as-is; Komga + Kavita notifiers shipped Phase 6 (rest reference-preserved per `.planning/reference/sonarr-vertical-slices/`) |

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
| Build | MSBuild (backend), Webpack 5 + Babel (frontend), Yarn 1.22 (pinned via `package.json` `packageManager`) |
| Testing | NUnit |
| DI Container | DryIoc |
| Logging | NLog |
| Validation | FluentValidation |

## Prerequisites

- .NET SDK 10.0.203 (`winget install Microsoft.DotNet.SDK.10 --source winget`)
- Node.js 20.x or higher
- Yarn 1.22.x (npm install -g yarn, or via Node's bundled npm). The `packageManager` field in `package.json` pins `yarn@1.22.22+sha512.…`; do NOT switch to Yarn 4/Berry without a documented migration — `yarn.lock`, `.yarnrc`, and the project's scripts assume Yarn 1 classic.

## Running Locally

```bash
# 1. Install frontend deps
yarn install

# 2. Build backend & frontend
#    On macOS/Linux add -p:EnableWindowsTargeting=true — the net10.0-windows service
#    wrapper (src/NzbDrone/Mangarr.csproj) trips NETSDK1100 otherwise. (No-op on Windows.)
dotnet build src/Mangarr.sln --configuration Debug   # macOS/Linux: append -p:EnableWindowsTargeting=true
yarn build

# 3. Run
dotnet run --project src/NzbDrone.Console/Mangarr.Console.csproj
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
│   ├── Mangarr.Http/                  # REST base / middleware / auth
│   ├── Mangarr.Api.V5/                # REST API v5 (sole REST surface; ~59 controllers)
│   ├── Mangarr.RuntimePatches/        # Runtime monkey-patches
│   ├── ServiceHelpers/               # Service install helpers
│   ├── Libraries/                    # Vendored binaries
│   ├── *.Test/ projects              # NUnit test projects
│   └── Mangarr.sln                    # Solution file
├── frontend/                         # React + TypeScript UI
│   ├── src/                          # 34 top-level dirs (see frontend/CLAUDE.md)
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
| Manga domain logic | [src/NzbDrone.Core/Manga/](./src/NzbDrone.Core/Manga/) |
| Chapter / ChapterFile logic | [src/NzbDrone.Core/Manga/](./src/NzbDrone.Core/Manga/) (Chapter.cs) / [src/NzbDrone.Core/MediaFiles/](./src/NzbDrone.Core/MediaFiles/) (ChapterFile.cs) |
| File handling (CBZ/CBR) | [src/NzbDrone.Core/MediaFiles/](./src/NzbDrone.Core/MediaFiles/) |
| Title/release parsing regex | [src/NzbDrone.Core/Parser/](./src/NzbDrone.Core/Parser/) (manga peers under [Parser/Manga/](./src/NzbDrone.Core/Parser/Manga/)) |
| Search filters / specs | [src/NzbDrone.Core/DecisionEngine/Manga/Specifications/](./src/NzbDrone.Core/DecisionEngine/Manga/Specifications/) (manga decision specs; engine under [DecisionEngine/Manga/](./src/NzbDrone.Core/DecisionEngine/Manga/)) |
| Indexer integrations | [src/NzbDrone.Core/Indexers/](./src/NzbDrone.Core/Indexers/) (`GatewayIndexer` under [Indexers/Gateway/](./src/NzbDrone.Core/Indexers/Gateway/) — sole `IIndexer`; in-process MangaDex/Comix scrapers retired in Phase 39) |
| Download client integrations | [src/NzbDrone.Core/Download/](./src/NzbDrone.Core/Download/) (`GatewayDownloadClient` under [Download/Clients/Gateway/](./src/NzbDrone.Core/Download/Clients/Gateway/) — sole download client; the in-process image downloader at `Download/Clients/InProcess/` was retired in Phase 39) |
| Notification providers | [src/NzbDrone.Core/Notifications/](./src/NzbDrone.Core/Notifications/) (Komga + Kavita live; rest reference-preserved) |
| Metadata source (MangaDex / AniList / MAL) | [src/NzbDrone.Core/MetadataSource/](./src/NzbDrone.Core/MetadataSource/) |
| Database schema | [src/NzbDrone.Core/Datastore/Migration/](./src/NzbDrone.Core/Datastore/Migration/) (`001_mangarr_baseline.cs` is the canonical migration per pre-v1 dev-migration policy) |
| API endpoints (V5) | [src/Mangarr.Api.V5/](./src/Mangarr.Api.V5/) |
| Auth / middleware / REST base | [src/Mangarr.Http/](./src/Mangarr.Http/) |
| App startup / DI registration | [src/NzbDrone.Host/Startup.cs](./src/NzbDrone.Host/Startup.cs), [src/NzbDrone.Host/Bootstrap.cs](./src/NzbDrone.Host/Bootstrap.cs) |
| React routes | [frontend/src/App/AppRoutes.tsx](./frontend/src/App/AppRoutes.tsx) |
| Manga library page (UI) | [frontend/src/Manga/Index/](./frontend/src/Manga/Index/) |
| Manga detail page (UI) | [frontend/src/Manga/Details/](./frontend/src/Manga/Details/) |
| Add Manga flow (UI) | [frontend/src/AddManga/](./frontend/src/AddManga/) |
| Settings pages (UI) | [frontend/src/Settings/](./frontend/src/Settings/) |
| Queue / History / Blocklist UI | [frontend/src/Activity/](./frontend/src/Activity/) |
| Wanted (Missing/CutoffUnmet) UI | [frontend/src/Wanted/](./frontend/src/Wanted/) |
| Manual search / import UI | [frontend/src/InteractiveSearch/](./frontend/src/InteractiveSearch/), [frontend/src/InteractiveImport/](./frontend/src/InteractiveImport/) |
| Shared UI components | [frontend/src/Components/](./frontend/src/Components/) |
| Redux store | [frontend/src/Store/](./frontend/src/Store/) |
| Custom hooks (useApiQuery, etc.) | [frontend/src/Helpers/](./frontend/src/Helpers/) |
| TypeScript types (API DTOs) | [frontend/src/typings/](./frontend/src/typings/) |

## Running Tests

Mangarr/Mangarr uses NUnit. The script at `scripts/test.sh` requires three params:

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
dotnet build src/Mangarr.sln --configuration Release         # Release build (macOS/Linux: add -p:EnableWindowsTargeting=true)
yarn build --env production                                 # Production frontend bundle
yarn lint && yarn lint-fix                                  # Lint
yarn stylelint                                              # CSS lint
yarn watch                                                  # Webpack watch mode
```

## Development Notes

- **Solution file**: `src/Mangarr.sln`
- **Database migrations**: Auto-applied on startup. Add new migration in `src/NzbDrone.Core/Datastore/Migration/`. Migrations are sequential and post-baseline (`001_mangarr_baseline.cs` → `010_v1_3_retire_in_process_cleanup.cs` currently — Migration 010 is the head, the Phase 39 one-shot that deletes the orphan in-process provider rows + drops the `ChapterDownloadState` table on upgrade); per the post-v1.0.0 policy, schema changes append a new sequential migration (the pre-v1 edit-`001`-in-place rule no longer applies).
- **Default data dir**: `C:\ProgramData\Mangarr` (Win) / `~/.config/Mangarr` (Linux/Mac). Logs in `<data>/logs/`.
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
| Complete a Sonarr → Mangarr migration step | Mark as completed in relevant docs + append a DIVERGENCE.md entry |
| Add new API endpoints | Update `src/Mangarr.Api.V5/CLAUDE.md` |
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
| `Manga/` (domain) | [src/NzbDrone.Core/Manga/CLAUDE.md](./src/NzbDrone.Core/Manga/CLAUDE.md) | Manga / Chapter / ChapterFile models (Sonarr `Tv/` was deleted in Phase 15 Plan 15-03) |
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
| Mangarr.Api.V5 | [src/Mangarr.Api.V5/CLAUDE.md](./src/Mangarr.Api.V5/CLAUDE.md) | REST API (sole REST surface; V3 wholesale-deleted Phase 15 Plan 15-06) |
| Mangarr.Http | [src/Mangarr.Http/CLAUDE.md](./src/Mangarr.Http/CLAUDE.md) | HTTP infrastructure |
| NzbDrone.Host | [src/NzbDrone.Host/CLAUDE.md](./src/NzbDrone.Host/CLAUDE.md) | App host / DI / startup |
| NzbDrone.Console | [src/NzbDrone.Console/CLAUDE.md](./src/NzbDrone.Console/CLAUDE.md) | Console entry point |
| NzbDrone.SignalR | [src/NzbDrone.SignalR/CLAUDE.md](./src/NzbDrone.SignalR/CLAUDE.md) | Real-time hub |

### Frontend Components
| Component | Documentation |
|-----------|---------------|
| Frontend Root | [frontend/CLAUDE.md](./frontend/CLAUDE.md) |
| `App/` (root + routing) | [frontend/src/App/CLAUDE.md](./frontend/src/App/CLAUDE.md) |
| `Manga/` (manga library — sibling-canonical) | [frontend/src/Manga/CLAUDE.md](./frontend/src/Manga/CLAUDE.md) (Sonarr `Series/` stub deleted in Phase 17.3 Plan 17.3-13) |
| `Chapter/` (chapter cells / hooks) | [frontend/src/Chapter/CLAUDE.md](./frontend/src/Chapter/CLAUDE.md) (Sonarr `Episode/` stub deleted in Phase 17.3 Plan 17.3-13) |
| `Components/` (shared UI) | [frontend/src/Components/CLAUDE.md](./frontend/src/Components/CLAUDE.md) |
| `Store/` (Redux) | [frontend/src/Store/CLAUDE.md](./frontend/src/Store/CLAUDE.md) |
| `Helpers/` (hooks) | [frontend/src/Helpers/CLAUDE.md](./frontend/src/Helpers/CLAUDE.md) |
| `Settings/` | [frontend/src/Settings/CLAUDE.md](./frontend/src/Settings/CLAUDE.md) |
| `AddManga/` (add-manga flow) | [frontend/src/AddManga/CLAUDE.md](./frontend/src/AddManga/CLAUDE.md) (Sonarr `AddSeries/` stub deleted in Phase 17.3 Plan 17.3-13) |
| `Activity/` (Queue/History/Blocklist) | [frontend/src/Activity/CLAUDE.md](./frontend/src/Activity/CLAUDE.md) |
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
| [.planning/codebase/](./.planning/codebase/) | 7-doc map of inherited Mangarr fork (STACK, ARCHITECTURE, STRUCTURE, CONVENTIONS, TESTING, INTEGRATIONS, CONCERNS) |
| [.planning/research/](./.planning/research/) | Web-verified domain research: STACK, FEATURES, ARCHITECTURE, PITFALLS, SUMMARY |
| [.planning/config.json](./.planning/config.json) | GSD workflow preferences (mode, granularity, model profile, agent toggles) |

**Design philosophy** (locked in PROJECT.md): *Preserve Mangarr's shape wherever it works; diverge only where the manga domain forces us.* This drives every gray-area call.

**Per-phase workflow:** `/gsd-discuss-phase N` → `/gsd-plan-phase N` → `/gsd-execute-phase N` → `/gsd-verify-work N` → **sonarr-consistency-audit** → `/gsd-extract-learnings`. Or `/gsd-progress` for the unified situational command.

The **sonarr-consistency-audit** step is mandatory before declaring a phase complete (see [.claude/skills/sonarr-consistency-audit/SKILL.md](./.claude/skills/sonarr-consistency-audit/SKILL.md)). It catches the class of bug where phase code diverges from Mangarr's canonical pattern AND its accompanying tests verify the divergent code — so "tests green" silently masks the divergence. Phase 2's `RefreshMangaCommand` migration-seed-vs-`TaskManager.defaultTasks` issue was the prompt for this skill. Run it on any phase that touches inherited Mangarr code (most do).

### Mandatory smoke gate before phase verification (Phase 23 retro 2026-05-17)

**Phase 20/22/23 retros each surfaced the same failure mode:** the orchestrator declares phase complete while the unit suite + `audit-new-fixtures.sh` fixture-execution gate have never been run live. Phase 20 had 9 fixture failures + 1 production bug surface in CI. Phase 22 had 3 newly-authored fixtures never run live. Phase 23 deferred both with a "CI handles this" rationale that the skill explicitly forbids.

**Textual exhortation in skill bodies did not prevent this twice in a row.** Mechanical enforcement now:

1. After all phase plans merge and BEFORE invoking `/gsd-verify-work N` or opening the PR, ALWAYS run:
   ```bash
   bash scripts/phase-smoke-gate.sh N
   ```
   The script first runs a **GH #252 pre-run cleanup gate** (Step 0 — `scripts/kill-orphan-test-processes.sh` sweeps orphan `testhost.exe` / `Mangarr.Console.exe` / Puppeteer-Playwright Chromium left by a prior crashed/cancelled run, so back-to-back runs self-heal instead of FATAL-ing on a leftover port-8989 listener), then the Playwright-provisioned check + port 8989 free check + full unit suite via `scripts/test.sh Windows Unit Test` + `scripts/audit-new-fixtures.sh` (which itself sweeps leftover testhost BETWEEN its UNIT and AUTOMATION steps to kill the MSB3027 DLL-lock false-failure). It writes `.planning/phases/N-*/SMOKE-GATE.json` with `{ passed: bool, failure_reasons: [...], unit_suite: {...}, fixture_gate: {...}, ... }`. It exits non-zero on any failure. To PROVE the no-leak property holds across repeated runs, run `bash scripts/validate-no-orphan-leaks.sh N [iterations] [settle-seconds]` (default 5×) — it exercises the gate back-to-back and asserts every leak class returns to baseline between runs (GH #252 acceptance harness; nightly CI job `validate_orphan_leaks` on `windows-latest`).

2. **The gsd-verifier subagent MUST read `.planning/phases/N-*/SMOKE-GATE.json`** before producing its VERIFICATION.md verdict. If the JSON is missing OR `passed` is `false`, the verdict is hard-coded to **FAIL** regardless of other findings. The verifier emits the failure reasons verbatim in the VERIFICATION.md so the user can see *which* gate failed.

3. **Do NOT defer either gate.** The "lands in CI" / "executor in worktree deferred this" / "build is the proxy" rationales are explicitly forbidden per Phase 20/22/23 retros. The orchestrator runs from the main repo where Playwright IS provisioned (`_tests/net10.0/playwright.ps1`), port 8989 IS host-controllable, and the full toolchain IS installed. These are not the worktree-executor's constraints.

4. **The smoke PLAN.md is a RECORD of what the gate proved, not a PLAN of what might run.** Pre-marking Tasks 1 or 2.5 as `DEFERRED` in the smoke PLAN.md is the exact anti-pattern this rule prevents. Worktree-executor SUMMARY.md files MAY document deferrals (those executors lack Playwright MCP), but the orchestrator-run phase smoke MUST execute them.
