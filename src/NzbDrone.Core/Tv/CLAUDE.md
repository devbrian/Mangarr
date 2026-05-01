# NzbDrone.Core/Tv

## Purpose

The **core domain layer** for series, episodes, seasons. Every feature in the system ultimately works against these models. **This is the most critical directory for the Sonarr → Mangarr migration.** Series will become Manga, Episode will become Chapter, Season will become Volume (optional).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Tv\`

## Files (~30 top-level)

### Domain Models

| File | Type | Notes |
|------|------|-------|
| `Series.cs` | `class Series : ModelBase` | The aggregate root. **40 properties.** Already has `MalIds: List<int>` and `AniListIds: List<int>` (manga-prep). |
| `Episode.cs` | `class Episode : ModelBase, IComparable` | 21 properties. Includes scene numbering, `AirDate`, `Runtime`, `FinaleType`. |
| `Season.cs` | `class Season : IEmbeddedDocument` | **Embedded document**, not its own table. Just `{ SeasonNumber, Monitored, Images }`. Stored as JSON column on Series. |
| `Actor.cs` | DTO | Cast list entry on Series |
| `Ratings.cs` | DTO | Embedded ratings (votes, value) |
| `SeriesStatusType.cs` | enum | `Continuing` / `Ended` / `Upcoming` / `Deleted` |
| `SeriesTypes.cs` | enum | `Standard` / `Daily` / `Anime` |
| `MonitorTypes.cs` (`AddSeriesOptions.cs`) | enum + DTO | Monitor presets: `all`, `future`, `missing`, `existing`, `recent`, `pilot`, `firstSeason`, `lastSeason`, `monitorSpecials`, `unmonitorSpecials`, `none` |

### Series Service Layer

| File | Purpose |
|------|---------|
| `ISeriesService.cs` / `SeriesService.cs` | CRUD, lookup, monitoring, statistics |
| `SeriesRepository.cs` | Dapper repo |
| `AddSeriesService.cs` | Flow for adding new series (resolve metadata, build path, persist, queue refresh) |
| `RefreshSeriesService.cs` | Pull updated metadata from `IProvideSeriesInfo` (TVDB / SkyHook) |
| `MoveSeriesService.cs` | Move series files between root folders |
| `SeriesEditedService.cs` | Apply post-edit consequences (rename, refresh) |
| `SeriesPathBuilder.cs` | Compute series folder name from naming config |
| `SeriesTitleNormalizer.cs` | Normalize title for matching (lowercase, strip articles, etc.) |
| `SeriesTitleSlugValidator.cs` / `AddSeriesValidator.cs` | FluentValidation rules |

### Episode Service Layer

| File | Purpose |
|------|---------|
| `IEpisodeService.cs` / `EpisodeService.cs` | CRUD, monitor toggling, find-by-numbers |
| `EpisodeRepository.cs` | Dapper repo |

### Event Handlers

| File | Reacts to |
|------|-----------|
| `SeriesAddedHandler.cs` | `SeriesAddedEvent` → trigger initial refresh + scan |
| `SeriesScannedHandler.cs` | `SeriesScannedEvent` → re-evaluate monitoring |

### Sub-folders

| Folder | Contents |
|--------|---------|
| `Commands/` | `RefreshSeriesCommand`, `MoveSeriesCommand`, etc. |
| `Events/` | `SeriesAddedEvent`, `SeriesDeletedEvent`, `SeriesUpdatedEvent`, `SeriesEditedEvent`, `SeriesRefreshStartingEvent`, etc. |

## Series.cs — Key Properties

```csharp
public class Series : ModelBase
{
    // External IDs
    public int TvdbId { get; set; }
    public int TvRageId { get; set; }
    public int TvMazeId { get; set; }
    public string ImdbId { get; set; }
    public int TmdbId { get; set; }
    public List<int> MalIds { get; set; }       // ← MANGA-PREP
    public List<int> AniListIds { get; set; }   // ← MANGA-PREP

    // Metadata
    public string Title { get; set; }
    public string CleanTitle { get; set; }
    public string SortTitle { get; set; }
    public string Overview { get; set; }
    public string AirTime { get; set; }
    public string TitleSlug { get; set; }      // URL-safe identifier
    public string Network { get; set; }
    public Language OriginalLanguage { get; set; }
    public string OriginalCountry { get; set; }

    // Status / monitoring
    public SeriesStatusType Status { get; set; }
    public bool Monitored { get; set; }
    public NewItemMonitorTypes MonitorNewItems { get; set; }

    // Quality / content
    public int QualityProfileId { get; set; }
    public SeriesTypes SeriesType { get; set; }
    public string Certification { get; set; }
    public List<string> Genres { get; set; }
    public List<Actor> Actors { get; set; }
    public int Year { get; set; }
    public int Runtime { get; set; }

    // Paths
    public string Path { get; set; }
    public string RootFolderPath { get; set; }

    // Dates
    public DateTime Added { get; set; }
    public DateTime? FirstAired { get; set; }
    public DateTime? LastAired { get; set; }
    public DateTime? LastInfoSync { get; set; }

    // Collections
    public List<MediaCover> Images { get; set; }
    public List<Season> Seasons { get; set; }   // embedded
    public HashSet<int> Tags { get; set; }
    public Ratings Ratings { get; set; }

    // Other
    public bool UseSceneNumbering { get; set; }
    public bool SeasonFolder { get; set; }
}
```

## Episode.cs — Key Properties

```csharp
public class Episode : ModelBase, IComparable<Episode>
{
    public int SeriesId { get; set; }
    public int TvdbId { get; set; }
    public int EpisodeFileId { get; set; }   // 0 if no file
    public LazyLoaded<Series> Series { get; set; }
    public LazyLoaded<EpisodeFile> EpisodeFile { get; set; }

    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public int? AbsoluteEpisodeNumber { get; set; }

    // Scene numbering (for releases that don't follow TVDB)
    public int? SceneSeasonNumber { get; set; }
    public int? SceneEpisodeNumber { get; set; }
    public int? SceneAbsoluteEpisodeNumber { get; set; }
    public bool UnverifiedSceneNumbering { get; set; }

    // Aired-vs-broadcast remapping
    public int? AiredAfterSeasonNumber { get; set; }
    public int? AiredBeforeSeasonNumber { get; set; }
    public int? AiredBeforeEpisodeNumber { get; set; }

    // Content
    public string Title { get; set; }
    public string Overview { get; set; }
    public DateTime? AirDate { get; set; }     // local
    public DateTime? AirDateUtc { get; set; }
    public int? Runtime { get; set; }
    public EpisodeFinaleType? FinaleType { get; set; }

    // Status
    public bool Monitored { get; set; }
    public bool HasFile => EpisodeFileId > 0;
    public bool? Grabbed { get; set; }
    public DateTime? LastSearchTime { get; set; }
    public Ratings Ratings { get; set; }
    public List<MediaCover> Images { get; set; }
}
```

## Season.cs — Embedded Document

```csharp
public class Season : IEmbeddedDocument
{
    public int SeasonNumber { get; set; }
    public bool Monitored { get; set; }
    public List<MediaCover> Images { get; set; }
}
```

Persisted as a JSON column on the `Series` table — there is **no `Seasons` table**.

## Common Operations

### Adding a Series
```csharp
_addSeriesService.AddSeries(new Series {
    TvdbId = 12345,
    Title = "My Show",
    QualityProfileId = qp.Id,
    Path = "/library/My Show",
    Monitored = true,
    AddOptions = new AddSeriesOptions {
        Monitor = MonitorTypes.Future,
        SearchForMissingEpisodes = true
    }
});
// → publishes SeriesAddedEvent → triggers RefreshSeriesCommand → scans disk
```

### Toggling Episode Monitored
```csharp
_episodeService.SetEpisodeMonitored(episodeId, monitored: true);
```

### Refreshing Metadata
Issue `RefreshSeriesCommand` with optional `SeriesIds`. The handler calls `RefreshSeriesService.RefreshSeriesInfo`, which fetches from `IProvideSeriesInfo` and reconciles episodes.

## Manga Adaptation Plan

### Renames
| File | Manga Name |
|------|-----------|
| `Series.cs` | `Manga.cs` |
| `Episode.cs` | `Chapter.cs` |
| `Season.cs` | `Volume.cs` |
| `SeriesService.cs` | `MangaService.cs` |
| `EpisodeService.cs` | `ChapterService.cs` |
| `RefreshSeriesService.cs` | `RefreshMangaService.cs` |
| `AddSeriesService.cs` | `AddMangaService.cs` |
| `SeriesAddedHandler.cs` | `MangaAddedHandler.cs` |

### Series → Manga Property Changes
| Remove | Add |
|--------|-----|
| `TvRageId`, `TvMazeId` (TV-only) | `MangaDexId : Guid?` |
| `Network` | `Author : string` |
| `AirTime` | `Artist : string` |
| `UseSceneNumbering` | `MangaType : enum` (Manga/Manhwa/Manhua/OEL) |
| `SeasonFolder` (rename) | `VolumeFolder : bool` |
| `SeriesType` (rename) | `Demographic : enum` (Shounen/Seinen/Shoujo/Josei) |
|  | `PublicationStatus` (Ongoing/Completed/Hiatus) |

`MalIds` and `AniListIds` already exist — keep them.

### Episode → Chapter Property Changes
| Remove | Add |
|--------|-----|
| `AirDate`, `AirDateUtc` | `ReleaseDate : DateTime?` |
| `SceneSeasonNumber`, `SceneEpisodeNumber`, `SceneAbsoluteEpisodeNumber` | `ScanlationGroup : string` |
| `AiredAfter/BeforeSeasonNumber` | `PageCount : int?` |
| `FinaleType`, `Runtime` | `IsOneShot : bool` |
| `EpisodeNumber` (rename) | `ChapterNumber : decimal` (allow 1.5, 24.5 etc.) |
| `SeasonNumber` (rename) | `VolumeNumber : int?` |

### Database Migration Strategy

Because schema migrations run sequentially, the migration approach should be:
1. **Phase 1**: Add new manga columns to `Series` / `Episodes` (additive, non-breaking)
2. **Phase 2**: Rebuild views/queries to project both names
3. **Phase 3**: Rename tables/columns + drop legacy via `224_rename_series_to_manga.cs` etc.
4. **Phase 4**: Rename C# types & adjust API resources

Until Phase 3, keep the C# class names `Series` / `Episode` to avoid touching every callsite at once.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../MediaFiles/CLAUDE.md](../MediaFiles/CLAUDE.md) — `EpisodeFile` is linked here
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses release titles into Series/Episode
- [../Datastore/CLAUDE.md](../Datastore/CLAUDE.md) — Storage / migrations
- [../MetadataSource/CLAUDE.md](../MetadataSource/CLAUDE.md) — Source of metadata for series/episodes
- [../../Sonarr.Api.V5/CLAUDE.md](../../Sonarr.Api.V5/CLAUDE.md) — `SeriesController` / `EpisodeController`
- [../../../frontend/src/Series/CLAUDE.md](../../../frontend/src/Series/CLAUDE.md) — Frontend mirror
