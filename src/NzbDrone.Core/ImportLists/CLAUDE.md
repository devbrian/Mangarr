# NzbDrone.Core/ImportLists

## Purpose

**Import lists** — automatically add new series to the library by ingesting from external lists (Trakt, AniList watchlist, custom JSON feeds, etc.). Uses the **ThingiProvider** plugin pattern.

For Mangarr, import lists for **MangaDex / AniList manga / MyAnimeList manga** are needed.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\ImportLists\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `IImportList.cs` / `ImportListBase.cs` | Provider interface + base |
| `HttpImportListBase.cs` | Generic HTTP-based list |
| `ImportListBase.cs` (different) | Service-base |
| `IImportListFactory.cs` / `ImportListFactory.cs` | ThingiProvider factory |
| `IImportListRepository.cs` / `ImportListRepository.cs` | DB persistence |
| `ImportListDefinition.cs` | Persisted config |
| `IImportListStatusService.cs` / `ImportListStatusService.cs` | Track health |
| `ImportListService.cs` | Orchestrator |
| `FetchAndParseImportListService.cs` | Periodic sync |
| `ImportListExclusion.cs`, `ImportListExclusionService.cs`, `ImportListExclusionRepository.cs` | "Don't auto-add this series" list |
| `Exclusions/` | Exclusion subdirectory |
| `ImportListItems/` | Imported item entities |

## Subdirectories — List Sources

| Folder | Source |
|--------|--------|
| `AniList/` | AniList API |
| `Custom/` | User-supplied JSON URL |
| `Plex/` (if present) | Plex watchlist |
| `Mangarr/` (if present) | Pull from another Mangarr instance |
| `Trakt/` (often) | Trakt lists |
| `Imdb/` | IMDB list URL |

## Provider Anatomy

```csharp
public class MyList : HttpImportListBase<MyListSettings>
{
    public override string Name => "My List";
    public override ImportListType ListType => ImportListType.Other;
    public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

    public override IList<ImportListItemInfo> Fetch() { /* … */ }
    public override IImportListRequestGenerator GetRequestGenerator() { /* … */ }
    public override IParseImportListResponse GetParser() { /* … */ }
}
```

`ImportListItemInfo` carries `Title`, `Year`, `TvdbId`, `ImdbId`, `TmdbId`, etc.

## Sync Flow

```
Scheduler ticks (ImportListSyncCommand)
    ↓
ImportListSyncService.Sync()
    ↓
For each enabled list:
    ├─ FetchAndParseImportListService.Fetch() → List<ImportListItemInfo>
    ├─ Filter by ImportListExclusion table
    ├─ Filter by already-existing series
    ├─ Lookup metadata via IProvideSeriesInfo (TVDB / SkyHook)
    └─ AddSeriesService.AddSeries(...) for each new
```

## Adding a New Import List

1. Create folder `ImportLists/MyList/`.
2. `MyListSettings.cs` (URL, auth tokens, list ID).
3. `MyList.cs` extends `HttpImportListBase<MyListSettings>`.
4. Add request generator and parser.
5. Auto-discovered. Tests under `NzbDrone.Core.Test/ImportListTests/MyListTests/`.

## Manga Adaptation Plan

### New Lists for Mangarr

| Source | Notes |
|--------|-------|
| **MangaDex** custom list / user list | High priority — primary Mangarr metadata source |
| **AniList manga lists** | Different GraphQL than anime — distinct list type |
| **MyAnimeList manga lists** | Public lists, REST API |
| **MangaUpdates** lists | Trickier (no public API, scrape) |
| **Custom JSON feed** | Already exists; just ensure schema includes manga IDs |

### Item Mapping

`ImportListItemInfo` needs to expose manga IDs:
- `MangaDexId : Guid?`
- `AniListMangaId : int?`
- `MalMangaId : int?`

(The Mangarr `Series` model already has `MalIds` and `AniListIds` — extending `ImportListItemInfo` is straightforward.)

### Auto-Add Workflow

`AddSeriesService.AddSeries(...)` is called once metadata is resolved. For Mangarr, add a manga-aware code path:
1. List returns `ImportListItemInfo` with manga IDs
2. Lookup via `MangaDexProxy.GetMangaInfo(...)` or similar
3. `AddMangaService.AddManga(...)` (new method) creates the entity

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../MetadataSource/CLAUDE.md](../MetadataSource/CLAUDE.md) — Used to look up series details
- [../Tv/CLAUDE.md](../Tv/CLAUDE.md) — Adds via `AddSeriesService`
- [../ThingiProvider/](../ThingiProvider/) — Provider plugin base
