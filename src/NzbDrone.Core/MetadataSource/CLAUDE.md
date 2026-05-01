# NzbDrone.Core/MetadataSource

## Purpose

External metadata provider integrations — fetch series/episode info from third-party APIs. Sonarr's primary source is **TVDB** via the **SkyHook** proxy (a Sonarr-hosted API that wraps TVDB).

For Mangarr this is a **CRITICAL migration target** — TV metadata sources must be replaced with manga sources (MangaDex, AniList, MyAnimeList, MangaUpdates, etc.).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MetadataSource\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `IProvideSeriesInfo.cs` | Interface — fetch series + episodes by external ID |
| `ISearchForNewSeries.cs` | Interface — text-search to add a new series |
| (No top-level implementation; SkyHook is the implementation) |

## Subdirectories

### `SkyHook/`
The **SkyHook** wrapper for TVDB / TheMovieDB. SkyHook is a Sonarr-hosted shim service at `https://skyhook.sonarr.tv/v1/tvdb/...` that normalizes TVDB/TMDB data into Sonarr's format and handles caching.

| File | Purpose |
|------|---------|
| `SkyHookProxy.cs` | Implements `IProvideSeriesInfo` + `ISearchForNewSeries` |
| `Resource/` | API response DTOs |
| `Resource/ShowResource.cs` | Series response |
| `Resource/EpisodeResource.cs` | Episode response |

The proxy:
- Uses `IHttpClient` to call SkyHook
- Authenticates with Sonarr API key headers
- Translates API DTOs to domain `Series` + `List<Episode>`
- Handles caching, rate limiting, error normalization

## How It's Used

### Adding a Series
```csharp
// User searches for a title → see UI list
ISearchForNewSeries searcher = …;
List<Series> matches = searcher.SearchForNewSeries("Show name");

// User picks one → AddSeriesService persists it
// Then refresh fetches episode list
RefreshSeriesService.RefreshSeriesInfo(seriesId);
   ↓
IProvideSeriesInfo provider = …;
var (series, episodes) = provider.GetSeriesInfo(tvdbId);
   ↓
Reconcile fetched episodes with DB (insert new, update changed, delete removed)
```

### Refreshing Existing
Triggered periodically or on-demand:
- Cmd: `RefreshSeriesCommand` → `RefreshSeriesService` → `IProvideSeriesInfo.GetSeriesInfo(tvdbId)`

## SkyHook Endpoints (current shape)

```
GET https://skyhook.sonarr.tv/v1/tvdb/shows/en/{tvdbId}
GET https://skyhook.sonarr.tv/v1/tvdb/search/?term=foo
GET https://skyhook.sonarr.tv/v1/tvdb/changes/?since=…
```

## Manga Adaptation Plan

### Strategy

The **interface design transfers** — `IProvideSeriesInfo` / `ISearchForNewSeries` are abstract enough. Implement new providers that conform to these interfaces (or rename them to `IProvideMangaInfo` / `ISearchForNewManga`).

### Recommended Manga Sources

| Source | API Quality | Notes |
|--------|-------------|-------|
| **MangaDex** | Excellent (official, REST + free, well-documented) | Primary recommendation. Catalog is massive. |
| **AniList** | Excellent (GraphQL, free with rate limit) | Strong cross-references; tracking. |
| **MyAnimeList** | Good (REST, app-key required) | Popular, established. |
| **MangaUpdates** | OK (HTML scrape; no public API) | Authoritative for chapter releases. |
| **Kitsu** | OK (REST) | Lower priority. |
| **Baka-Updates** | (deprecated) | Don't use. |

### Implementation Plan

1. Create `MetadataSource/MangaDex/MangaDexProxy.cs` implementing `IProvideSeriesInfo` (or new `IProvideMangaInfo`).
2. Create `Resource/` DTOs matching MangaDex API shape.
3. Map MangaDex data → Mangarr's `Series` (or `Manga`) + `List<Episode>` (or `List<Chapter>`).
4. Add `MetadataSource/AniList/` similarly.
5. Add UI entry in `Settings/MetadataSource/` to choose primary metadata source.
6. Update `RefreshSeriesService` to dispatch to the configured source.
7. Update `ISearchForNewSeries` consumers (SeriesLookupController) to call all configured sources.

### Schema Considerations

- Series table already has `MalIds` (List<int>) and `AniListIds` (List<int>) — add `MangaDexId` (Guid) too.
- `Series.TvdbId` becomes optional / unused for manga.
- `TitleSlug` continues to work (URL-safe string).

### Multi-Source Strategy

Different sources have different strengths:
- **MangaDex** — chapter listings (most accurate)
- **AniList** — relations / status / cross-IDs
- **MangaUpdates** — release-tracking & alternate names

A **federated source** approach (combine info from multiple sources, with user-chosen primary for each field) would be ideal. This is a non-trivial architectural choice — likely deferred to post-MVP.

### Skyhook Decommissioning

The Sonarr-hosted SkyHook service is TV-only. For Mangarr, either:
- (a) Talk to MangaDex/AniList **directly** from the client
- (b) Build a "MangaHook" cloud proxy that normalizes multiple sources

(a) is simpler and avoids cloud dependencies. (b) would centralize caching and rate limiting but requires hosting.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Tv/CLAUDE.md](../Tv/CLAUDE.md) — Series/Episode populated from this
- [../Tv/RefreshSeriesService.cs](../Tv/RefreshSeriesService.cs) — Caller
- [../../Sonarr.Api.V5/Series/SeriesLookupController.cs](../../Sonarr.Api.V5/Series/SeriesLookupController.cs) — Search-add UX entrypoint
- [../../../frontend/src/AddSeries/CLAUDE.md](../../../frontend/src/AddSeries/CLAUDE.md) — Frontend "Add" flow
