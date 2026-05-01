# NzbDrone.Core

## Purpose

This is the **core business logic layer** of the application - the largest and most important project. It contains all domain models, services, and business rules for managing TV series (to be adapted for manga).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\`

## Directory Structure

### Domain Models (`Tv/`)
| File | Purpose |
|------|---------|
| `Series.cs` | Main series entity (→ Manga) |
| `Episode.cs` | Episode entity (→ Chapter) |
| `Season.cs` | Season embedded document (→ Volume) |
| `SeriesService.cs` | Series CRUD operations |
| `EpisodeService.cs` | Episode management |
| `RefreshSeriesService.cs` | Metadata sync from external sources |

### Parser (`Parser/`)
| File | Purpose |
|------|---------|
| `Parser.cs` | Title parsing (71KB, regex-heavy) |
| `ParsingService.cs` | Maps parsed info to domain entities |
| `Model/ReleaseInfo.cs` | Release metadata from indexer |
| `Model/RemoteEpisode.cs` | Parsed release linked to series/episodes |
| `Model/ParsedEpisodeInfo.cs` | Extracted title data |

### Decision Engine (`DecisionEngine/`)
| File | Purpose |
|------|---------|
| `DownloadDecisionMaker.cs` | Main decision orchestrator (750+ lines) |
| `DownloadDecision.cs` | Decision result model |
| `Specifications/` | Individual decision rules (~32 specs) |

Key Specifications:
- `QualityAllowedByProfileSpecification.cs` - Quality profile check
- `UpgradableSpecification.cs` - Upgrade logic
- `BlocklistSpecification.cs` - Blocked releases
- `AirDateSpecification.cs` - Schedule checks
- `TorrentSeedingSpecification.cs` - Seed requirements

### Indexers (`Indexers/`)
| File | Purpose |
|------|---------|
| `IIndexer.cs` | Indexer interface |
| `IndexerBase.cs` | Base implementation |
| `Nyaa/` | Anime torrent indexer |
| `Torznab/` | Torznab protocol |
| `Newznab/` | Usenet indexer protocol |
| `IndexerSearch/` | Search execution |

### Download (`Download/`)
| File | Purpose |
|------|---------|
| `IDownloadClient.cs` | Download client interface |
| `DownloadService.cs` | Download execution |
| `CompletedDownloadService.cs` | Post-download processing |
| `DownloadClientProvider.cs` | Client selection |
| `Clients/` | Client implementations (qBittorrent, SABnzbd, etc.) |

### Media Files (`MediaFiles/`)
| File | Purpose |
|------|---------|
| `EpisodeFile.cs` | Physical file entity |
| `DiskScanService.cs` | Library scanning |
| `EpisodeImport/` | File import logic |
| `RenameEpisodeFileService.cs` | File renaming |
| `EpisodeFileMovingService.cs` | File organization |

### Data Layer (`Datastore/`)
| File | Purpose |
|------|---------|
| `ModelBase.cs` | Base entity class |
| `BasicRepository.cs` | Generic repository |
| `DbFactory.cs` | Database connection factory |
| `Migration/` | FluentMigrator schema migrations |

### Messaging (`Messaging/`)
| File | Purpose |
|------|---------|
| `Events/IEventAggregator.cs` | Event publisher |
| `Events/IHandle.cs` | Event subscriber interface |
| `Commands/` | Command definitions |

### Other Key Directories
| Directory | Purpose |
|-----------|---------|
| `Profiles/` | Quality and delay profiles |
| `Qualities/` | Quality definitions |
| `Languages/` | Language support |
| `Notifications/` | Notification providers |
| `History/` | Download/import history |
| `Queue/` | Download queue tracking |
| `Jobs/` | Scheduled tasks |
| `HealthCheck/` | System health monitoring |
| `CustomFormats/` | Custom release scoring |
| `AutoTagging/` | Automatic tagging |
| `RootFolders/` | Library root folder management |
| `MetadataSource/` | External metadata APIs |

## Key Interfaces

```csharp
// Core domain services
ISeriesService      // Series CRUD
IEpisodeService     // Episode management
IParsingService     // Release → Entity mapping

// Plugin providers
IIndexer            // Fetch releases
IDownloadClient     // Download management
INotification       // Send notifications
IImportList         // Import from external lists

// Decision making
IDownloadDecisionEngineSpecification  // Single decision rule
IMakeDownloadDecision                 // Decision orchestrator

// Events
IEventAggregator    // Publish events
IHandle<TEvent>     // Subscribe to events
```

## Data Flow

```
Indexer.Fetch()
    ↓
List<ReleaseInfo>
    ↓
Parser.ParseTitle() → ParsedEpisodeInfo
    ↓
ParsingService.Map() → RemoteEpisode
    ↓
DecisionEngine.GetDecision() runs all Specifications
    ↓
DownloadDecision (Approved/Rejected)
    ↓
DownloadService.DownloadReport()
    ↓
CompletedDownloadService handles completion
    ↓
ImportService → EpisodeFile created
    ↓
Events published (EpisodeImportedEvent, etc.)
```

## Key Patterns

### Lazy Loading
```csharp
public LazyLoaded<Series> Series { get; set; }
public LazyLoaded<List<Episode>> Episodes { get; set; }
```

### Event Publishing
```csharp
_eventAggregator.PublishEvent(new SeriesAddedEvent(series));
```

### Specification Pattern
```csharp
public class MySpecification : IDownloadDecisionEngineSpecification
{
    public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject)
    {
        if (condition)
            return DownloadSpecDecision.Accept();
        return DownloadSpecDecision.Reject("Reason");
    }
}
```

## Manga Adaptation Focus

### High Priority Changes
1. **Tv/** - Rename Series→Manga, Episode→Chapter, Season→Volume
2. **Parser/** - New regex patterns for manga release naming
3. **Indexers/** - Add manga site scrapers (MangaDex, etc.)
4. **MediaFiles/** - Handle image files/CBZ instead of video

### Reusable As-Is
- Decision engine architecture
- Download client integration
- Event messaging system
- Database abstraction
- Quality profile system
- Notification system

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) - Overall architecture
- [Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) - API controllers
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) - HTTP infrastructure
