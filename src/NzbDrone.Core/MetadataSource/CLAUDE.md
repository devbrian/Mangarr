# NzbDrone.Core/MetadataSource

## Purpose

External metadata provider integrations — fetch series/episode info from third-party APIs. Mangarr's primary source is **TVDB** via the **SkyHook** proxy (a Mangarr-hosted API that wraps TVDB).

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
The **SkyHook** wrapper for TVDB / TheMovieDB. SkyHook is a Mangarr-hosted shim service at `https://skyhook.sonarr.tv/v1/tvdb/...` that normalizes TVDB/TMDB data into Mangarr's format and handles caching.

| File | Purpose |
|------|---------|
| `SkyHookProxy.cs` | Implements `IProvideSeriesInfo` + `ISearchForNewSeries` |
| `Resource/` | API response DTOs |
| `Resource/ShowResource.cs` | Series response |
| `Resource/EpisodeResource.cs` | Episode response |

The proxy:
- Uses `IHttpClient` to call SkyHook
- Authenticates with Mangarr API key headers
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

The Mangarr-hosted SkyHook service is TV-only. For Mangarr, either:
- (a) Talk to MangaDex/AniList **directly** from the client
- (b) Build a "MangaHook" cloud proxy that normalizes multiple sources

(a) is simpler and avoids cloud dependencies. (b) would centralize caching and rate limiting but requires hosting.

## Phase 2 Additions (NEW IMetadataSource ThingiProvider family)

Phase 2 introduces a NEW pluggable provider type per D-14. The existing
concrete-singleton `IProvideSeriesInfo`/`ISearchForNewSeries` (SkyHookProxy) stays
UNTOUCHED until Phase 8 cutover.

### Files

| File | Purpose |
|------|---------|
| `IProvideMangaInfo.cs` | Split contract — `GetMangaInfo(string sourceId) -> Tuple<Manga, List<Chapter>>` per D-14 |
| `ISearchForNewManga.cs` | Split contract — Search by title or by cross-source ID per D-14 |
| `IMetadataSource.cs` | Composite interface; required for ThingiProvider auto-discovery (Pitfall 4) |
| `MetadataSourceDefinition.cs` | ProviderDefinition with IsPrimary bool per D-15 |
| `MetadataSourceBase.cs` | Abstract base implementing the seven IProvider members |
| `HttpMetadataSourceBase.cs` | Sibling (NOT subclass) of HttpAggregatorBase per RESEARCH §Open Question 1 |
| `IMetadataSourceFactory.cs` / `MetadataSourceFactory.cs` | ProviderFactory + `SetPrimary` at-most-one invariant per D-15 |
| `IMetadataSourceRepository.cs` / `MetadataSourceRepository.cs` | ProviderRepository<MetadataSourceDefinition> with `FindByName` + `GetPrimary` |
| `MangaNotFoundException.cs` | Thrown by providers on upstream 404 |
| `MangaDex/`, `AniList/`, `MyAnimeList/` | v1 provider implementations (Plans 02-06..02-08) |
| `CrossSourceIdResolver.cs` | Jaro-Winkler ≥0.85 + 2-of-3 multi-axis confirm per D-19..D-22 (Plan 02-09) |

### IsPrimary Invariant (D-15)

At most ONE row in the `MetadataSources` table may have `IsPrimary=true`. The DB
allows the invariant to be broken (Migration 002 has no DB-level constraint); the factory
restores it on every `SetPrimary(id)` call: demote ALL, then promote target, transactional
via `Update(IEnumerable)` → `IProviderRepository.UpdateMany`. Wave 0
`MetadataSourceFactoryFixture.SetPrimary_demotes_prior_primary_when_promoting_another`
verifies (mitigates threat T-CONFIG-DRIFT-01).

### Why HttpMetadataSourceBase Is a Sibling

`HttpAggregatorBase : HttpIndexerBase : IndexerBase` — extending it would register
metadata sources as `IIndexer` via DryIoc auto-discovery. We want the two ThingiProvider
families separate. Per RESEARCH §Open Question 1, `HttpMetadataSourceBase` duplicates
~30 lines of SourceKey + UA injection from HttpAggregatorBase — intentional cost
to keep the registries clean.

### Phase 8 Cutover

- DELETE `IProvideSeriesInfo`, `ISearchForNewSeries`, `SkyHookProxy` (TV side)
- KEEP `IProvideMangaInfo`, `ISearchForNewManga` (already manga-named — no rename needed)

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga/Chapter populated from this (the Sonarr `Tv/` analog was deleted in Phase 15)
- [../Manga/RefreshMangaService.cs](../Manga/RefreshMangaService.cs) — Caller (replaced the deleted Sonarr `Tv/RefreshSeriesService.cs`)
- [../../Mangarr.Api.V5/Manga/MangaLookupController.cs](../../Mangarr.Api.V5/Manga/MangaLookupController.cs) — Search-add UX entrypoint (replaced the deleted Sonarr `Series/SeriesLookupController.cs`)
- [../../../frontend/src/AddManga/CLAUDE.md](../../../frontend/src/AddManga/CLAUDE.md) — Frontend "Add" flow (Sonarr `AddSeries/` renamed in Phase 17.3)
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga / Chapter domain models consumed by `IProvideMangaInfo` / `ISearchForNewManga`
- [../Indexers/Http/HttpAggregatorBase.cs](../Indexers/Http/HttpAggregatorBase.cs) — Sibling Phase 1 base; intentional duplication source
- [../ThingiProvider/](../ThingiProvider/) — ProviderBase / ProviderDefinition / ProviderFactory / ProviderRepository
