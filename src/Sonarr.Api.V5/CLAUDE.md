# Sonarr.Api.V5

## Purpose

REST API controllers for version 5 of the API. This is the **current primary API** used by the frontend and external integrations.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\`

## Directory Structure

### Core Resources
| Directory | Purpose | Manga Equivalent |
|-----------|---------|------------------|
| `Series/` | Series CRUD, search, editor | Manga management |
| `Episodes/` | Episode operations | Chapter operations |
| `EpisodeFiles/` | Episode file management | Chapter file management |
| `Calendar/` | Upcoming episodes | Upcoming chapters |
| `Wanted/` | Missing/cutoff episodes | Missing chapters |

### Configuration
| Directory | Purpose |
|-----------|---------|
| `Profiles/` | Quality, language, delay profiles |
| `Qualities/` | Quality definitions |
| `CustomFormats/` | Custom format rules |
| `Tags/` | Tag management |
| `RootFolders/` | Library root folders |

### Download Pipeline
| Directory | Purpose |
|-----------|---------|
| `Indexers/` | Indexer configuration |
| `DownloadClient/` | Download client setup |
| `Blocklist/` | Blocked releases |
| `Queue/` | Download queue |
| `History/` | Download history |
| `ManualImport/` | Manual file import |

### System
| Directory | Purpose |
|-----------|---------|
| `Commands/` | Command execution |
| `System/` | System info, health, logs |
| `Logs/` | Log file access |
| `Health/` | Health checks |
| `Update/` | Update management |
| `Config/` | Configuration endpoints |

### Other
| Directory | Purpose |
|-----------|---------|
| `ImportLists/` | External list imports |
| `Notifications/` | Notification providers |
| `Metadata/` | Metadata providers |
| `Parse/` | Title parsing API |
| `FileSystem/` | File browser |
| `Localization/` | Language strings |

## Key Controllers

### SeriesController
```
GET    /api/v5/series           - List all series
GET    /api/v5/series/{id}      - Get single series
POST   /api/v5/series           - Add series
PUT    /api/v5/series/{id}      - Update series
DELETE /api/v5/series/{id}      - Delete series
GET    /api/v5/series/lookup    - Search for series
PUT    /api/v5/series/editor    - Bulk edit
DELETE /api/v5/series/editor    - Bulk delete
```

### EpisodeController
```
GET    /api/v5/episode          - List episodes (by seriesId)
GET    /api/v5/episode/{id}     - Get single episode
PUT    /api/v5/episode/{id}     - Update episode
PUT    /api/v5/episode/monitor  - Bulk monitor update
```

### CommandController
```
GET    /api/v5/command          - List commands
GET    /api/v5/command/{id}     - Get command status
POST   /api/v5/command          - Execute command
DELETE /api/v5/command/{id}     - Cancel command
```

## Resource Pattern

Each controller has a corresponding Resource (DTO):

```csharp
// Controller
public class SeriesController : RestController<SeriesResource>

// Resource
public class SeriesResource : RestResource
{
    public string Title { get; set; }
    public List<SeasonResource> Seasons { get; set; }
    // ...
}
```

## Mapping Convention

Resources map to/from domain models:

```csharp
// Domain → Resource
public static SeriesResource ToResource(this Series model)
{
    return new SeriesResource
    {
        Id = model.Id,
        Title = model.Title,
        // ...
    };
}

// Resource → Domain
public static Series ToModel(this SeriesResource resource)
{
    return new Series
    {
        Id = resource.Id,
        Title = resource.Title,
        // ...
    };
}
```

## Common Commands

Commands triggered via POST `/api/v5/command`:

| Command | Purpose |
|---------|---------|
| `RefreshSeries` | Refresh series metadata |
| `SeriesSearch` | Search for series releases |
| `EpisodeSearch` | Search for episode |
| `SeasonSearch` | Search for season |
| `RssSync` | Sync RSS feeds |
| `RenameFiles` | Rename episode files |
| `RescanSeries` | Rescan disk files |
| `MissingEpisodeSearch` | Search all missing |

## Manga Adaptation

### Rename Controllers
- `SeriesController` → `MangaController`
- `EpisodeController` → `ChapterController`
- `EpisodeFileController` → `ChapterFileController`

### New Endpoints Needed
- `/api/v5/manga/lookup` - Search manga metadata
- `/api/v5/chapter` - Chapter management
- `/api/v5/volume` - Volume management (if implemented)

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Overall architecture
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) - Base infrastructure
- [NzbDrone.Core/CLAUDE.md](../NzbDrone.Core/CLAUDE.md) - Business logic
- [frontend/CLAUDE.md](../../frontend/CLAUDE.md) - Frontend consuming this API
