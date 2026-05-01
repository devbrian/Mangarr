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

## Project Overview

**Mangarr** is a manga/manhwa/manhua library manager and downloader. It monitors manga reader and aggregator websites for new chapters of your favorite titles, automatically downloads, sorts, and organizes them. It can also be configured to automatically upgrade quality when better scans become available.

This project is a fork/migration of [Sonarr](https://github.com/Sonarr/Sonarr), adapted from television show management to manga management.

## Migration: Sonarr → Mangarr

### Conceptual Mapping

| Sonarr Concept | Mangarr Concept |
|----------------|-----------------|
| Series | Manga/Manhwa/Manhua (Title) |
| Season | Volume (optional) |
| Episode | Chapter |
| TV Database (TVDB) | Manga metadata sources (MangaDex, AniList, MyAnimeList) |
| Indexers (Usenet/Torrent) | Manga aggregator sites |
| Download Clients | Chapter downloaders (image scrapers) |
| Episode File | Chapter folder/CBZ |
| Quality (720p, 1080p) | Scan quality (raw, translated, official) |

### Key Differences from Sonarr

- **Content Type**: Manga chapters (images) instead of video files
- **Sources**: Web scrapers for manga sites instead of Usenet/BitTorrent indexers
- **File Format**: CBZ/CBR archives or image folders instead of video files
- **Metadata**: Manga-specific metadata (author, artist, genres, status)

## Technology Stack

- **Backend**: C# on .NET 10 framework
- **Frontend**: ReactJS with TypeScript
- **Database**: SQLite (default) or PostgreSQL
- **Build Tools**: MSBuild for backend, Webpack for frontend, Yarn for package management

## Prerequisites

- .NET SDK 10.0.203 (install via `winget install Microsoft.DotNet.SDK.10 --source winget`)
- Node.js 20.x or higher
- Yarn (enable with `corepack enable` or install via `npm i -g corepack`)

## Running Locally

### 1. Install Dependencies

```bash
# Install frontend dependencies
yarn install
```

### 2. Build the Project

```bash
# Build backend
dotnet build src/Sonarr.sln --configuration Debug

# Build frontend
yarn build
```

### 3. Run the Application

```bash
# Run the console application
dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj
```

The application will start and be accessible at **http://localhost:8989**

## Project Structure

- `src/` - Backend C# source code
  - `NzbDrone.Console/` - Console application entry point
  - `NzbDrone.Core/` - Core business logic
  - `NzbDrone.Host/` - Web host and API
  - `Sonarr.Api.V3/` - API v3 controllers
  - `Sonarr.Api.V5/` - API v5 controllers
  - `NzbDrone.Common/` - Shared utilities
- `frontend/` - React frontend source code
- `_output/` - Build output directory
- `_tests/` - Test output directory

## Running Tests

Sonarr uses nunit for its unit, integration, and automation test suite.

**From Visual Studio:**
- Navigate to Test Explorer and run or debug the tests you'd like to examine
- Tests can be run all at once or individually
- Uses the included nunit3testadapter NuGet package

**From Command Line:**

The test script is located at `scripts/test.sh` and requires 3 parameters:

1. **Platform**: `Windows`, `Linux`, or `Mac`
2. **Type**: `Unit`, `Integration`, or `Automation`
3. **Run Type**: `Coverage` or `Test`

```bash
# Set the test directory (required)
export TEST_DIR="./_tests/net10.0"

# Run unit tests
bash scripts/test.sh Windows Unit Test

# Run integration tests
bash scripts/test.sh Windows Integration Test

# Run tests with coverage
bash scripts/test.sh Windows Unit Coverage
```

Test results are written to `TestResult.xml` in the project root.

## Common Commands

```bash
# Clean and rebuild
yarn clean && yarn build

# Build for production
dotnet build src/Sonarr.sln --configuration Release
yarn build --env production
```

## Development Notes

- The solution file is located at `src/Sonarr.sln`
- Database migrations are handled automatically on startup
- Default data directory: `C:\ProgramData\Sonarr` (Windows) or `~/.config/Sonarr` (Linux/macOS)
- Logs are written to the data directory under `logs/`

## Documentation Maintenance Guidelines

When working on this project, follow these documentation practices:

### When to Update Documentation

| Action | Documentation Update Required |
|--------|------------------------------|
| Modify files in a directory | Update that directory's `CLAUDE.md` |
| Add new directory/feature | Create new `CLAUDE.md` in that directory |
| Rename/move files | Update cross-references in all affected docs |
| Change architecture | Update `PROJECT_CONTEXT.md` |
| Complete Sonarr→Mangarr migration | Mark as completed in relevant docs |
| Add new API endpoints | Update `src/Sonarr.Api.V5/CLAUDE.md` |
| Add new domain models | Update `src/NzbDrone.Core/CLAUDE.md` |
| Add new UI components | Update `frontend/src/Components/CLAUDE.md` |

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

## Usage/Patterns
[Code examples if applicable]

## Cross-References
- [Related Doc](./path/to/CLAUDE.md)
```

## Documentation Index

For detailed architecture and component documentation, see:

### Architecture
- [PROJECT_CONTEXT.md](./PROJECT_CONTEXT.md) - Overall architecture and data flow

### Backend Components
| Component | Documentation | Purpose |
|-----------|---------------|---------|
| NzbDrone.Core | [src/NzbDrone.Core/CLAUDE.md](./src/NzbDrone.Core/CLAUDE.md) | Core business logic, domain models |
| NzbDrone.Common | [src/NzbDrone.Common/CLAUDE.md](./src/NzbDrone.Common/CLAUDE.md) | Shared utilities |
| Sonarr.Api.V5 | [src/Sonarr.Api.V5/CLAUDE.md](./src/Sonarr.Api.V5/CLAUDE.md) | REST API controllers |
| Sonarr.Http | [src/Sonarr.Http/CLAUDE.md](./src/Sonarr.Http/CLAUDE.md) | HTTP infrastructure |
| NzbDrone.Host | [src/NzbDrone.Host/CLAUDE.md](./src/NzbDrone.Host/CLAUDE.md) | Application host |
| NzbDrone.Console | [src/NzbDrone.Console/CLAUDE.md](./src/NzbDrone.Console/CLAUDE.md) | Entry point |
| NzbDrone.SignalR | [src/NzbDrone.SignalR/CLAUDE.md](./src/NzbDrone.SignalR/CLAUDE.md) | Real-time messaging |

### Frontend Components
| Component | Documentation | Purpose |
|-----------|---------------|---------|
| Frontend Root | [frontend/CLAUDE.md](./frontend/CLAUDE.md) | React app overview |
| Series Module | [frontend/src/Series/CLAUDE.md](./frontend/src/Series/CLAUDE.md) | Series feature (→ Manga) |
| Episode Module | [frontend/src/Episode/CLAUDE.md](./frontend/src/Episode/CLAUDE.md) | Episode components (→ Chapter) |
| Components | [frontend/src/Components/CLAUDE.md](./frontend/src/Components/CLAUDE.md) | Shared UI components |
| Store | [frontend/src/Store/CLAUDE.md](./frontend/src/Store/CLAUDE.md) | Redux state management |
