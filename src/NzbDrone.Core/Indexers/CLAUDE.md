# NzbDrone.Core/Indexers

## Purpose

**Indexer plugins** — services that fetch lists of releases from external sites. Used both for periodic RSS sync (recurring search of "what's new") and on-demand search (find episode X). Indexers follow the **ThingiProvider** plugin pattern.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\`

> **Phase 39 (Plan 39-03/04) — in-process site-scraper indexers RETIRED.** Mangarr no longer
> runs in-process manga aggregator scrapers. `Indexers/Gateway/GatewayIndexer.cs` (Phase 37) is
> the **sole `IIndexer`** — it fans search/recent requests out to the external manga gateway,
> which owns the embedded browser + anti-bot clearance; **Mangarr ships zero embedded browser.**
> Deleted in Phase 39: the `MangaDex/` + `Comix/` in-process indexer dirs; the Comix anti-bot
> signer stack (`ComixPlaywrightSigner`/`CassettingComixSigner`/`IComixSigner`/`CassetteMode`);
> the `Http/` page-fetch coupling (`IHttpAggregator` marker + `HttpAggregatorBase` + `ChapterManifest`/
> `ChapterPage`/`ManifestExpiredException`); and the `Cloudflare/` clearance cascade. `Microsoft.Playwright`
> was dropped from `Mangarr.Core`. **`IHttpAggregatorSettings` SURVIVES** in `Http/HttpAggregatorSettingsBase.cs`
> — it is the per-`SourceKey` settings interface the 3 metadata sources implement for CF scoring,
> distinct from the deleted `IHttpAggregator` marker (the "named-gate" carve). `IndexerFactory`
> seeds only `GatewayIndexer`. The Sonarr Usenet/Torrent indexer tables below are reference-preserved
> fork heritage, NOT manga sources. See `DIVERGENCE.md` Phase 39 section.

## Files & Subdirectories

### Top-Level Infrastructure
| File | Purpose |
|------|---------|
| `IIndexer.cs` | Provider interface |
| `IndexerBase.cs` | Common base implementation |
| `HttpIndexerBase.cs` | Generic HTTP-based indexer with `IIndexerRequestGenerator` + `IParseIndexerResponse` |
| `IndexerRepository.cs` / `IndexerFactory.cs` | ThingiProvider persistence + lookup |
| `IndexerStatusService.cs` | Track which indexers are healthy |
| `IndexerStatusRepository.cs` | Persist failure state |
| `IIndexerRequestGenerator.cs` | Strategy: build HTTP requests for RSS / search |
| `IParseIndexerResponse.cs` | Strategy: parse response into `IList<ReleaseInfo>` |
| `IndexerCapabilities.cs` | Self-describes what the indexer supports |
| `FetchAndParseRssService.cs` | Periodic RSS sync runner |
| `IndexerHttpException.cs`, `IndexerSecondaryUrlException.cs`, etc. | Indexer-specific exceptions |
| `IndexerPageableRequest.cs`, `IndexerPageableRequestChain.cs` | Multi-page request orchestration |

### Subdirectories — Specific Indexers

| Folder | Type | Notes |
|--------|------|-------|
| `Newznab/` | Usenet | Standard Newznab API (most major NZB sites) |
| `Torznab/` | Torrent | Newznab-compatible torrent (Jackett, Prowlarr) |
| `TorrentRss/` | Torrent | Generic torrent RSS feed |
| `Nyaa/` | Torrent | **Anime tracker** — closest to manga release tracker patterns |
| `BroadcastheNet/` | Torrent | Private TV tracker |
| `HDBits/` | Torrent | Private HD tracker |
| `IPTorrents/` | Torrent | Private tracker |
| `FileList/` | Torrent | Romanian tracker |
| `Torrentleech/` | Torrent | Private tracker |
| `Fanzub/` | Usenet | Anime-specific |
| `Exceptions/` | — | Indexer-specific exceptions |
| `Gateway/` | Manga gateway | `GatewayIndexer` — the sole `IIndexer` (Phase 37; external manga gateway client). **SEARCH is multi-page since quick task 260620-ing:** bounded offset pagination (`0, L, 2L, …`) walked by the kept `HttpIndexerBase` paging engine via a dynamic `PageSize => GatewayRequestGenerator.ResolveEffectiveLimit(...)` (single source of truth, mirrors the MangaDex import-list precedent), stopping at the first short page; `/recent` remains single-request. See `DIVERGENCE.md`. |
| `Http/` | Settings interface only | Holds the SURVIVING `IHttpAggregatorSettings` per-`SourceKey` settings interface (`HttpAggregatorSettingsBase.cs`); the `IHttpAggregator` page-fetch marker + `HttpAggregatorBase<TSettings>` were deleted in Phase 39 Plan 39-04 |

### `Http/` — surviving settings interface (post-Phase-39)

The Phase-1 `HttpAggregatorBase<TSettings>` page-fetch base class and the `IHttpAggregator` page-fetch marker were **deleted in Phase 39 (Plan 39-04)** with the in-process indexers/downloader that consumed them. What survives in `Http/HttpAggregatorSettingsBase.cs` is the near-namesake **`IHttpAggregatorSettings`** — the per-`SourceKey` settings interface implemented by the 3 metadata-source settings (MangaDex/AniList/MyAnimeList) and read by `MangaDownloadDecisionMaker` for Custom-Format scoring. The named-gate dual-grep in Plan 39-04 mechanically proved the marker delete did not break this settings interface. `GatewayIndexer` (in `Gateway/`) does NOT extend `HttpAggregatorBase`; it implements the indexer contract directly.

## Indexer Anatomy

A typical indexer comprises:

```
MyIndexer/
├── MyIndexer.cs                    # Provider class
├── MyIndexerSettings.cs            # User-configurable settings + validator
├── MyIndexerRequestGenerator.cs    # Builds HTTP requests
└── MyIndexerParser.cs              # Parses response → ReleaseInfo[]
```

### Provider Class
```csharp
public class MyIndexer : HttpIndexerBase<MyIndexerSettings>
{
    public override string Name => "My Indexer";
    public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
    public override int PageSize => 100;

    public override IIndexerRequestGenerator GetRequestGenerator() => new MyIndexerRequestGenerator { Settings = Settings };
    public override IParseIndexerResponse GetParser() => new MyIndexerParser { Settings = Settings };
}
```

### Settings
```csharp
public class MyIndexerSettings : IIndexerSettings
{
    public string BaseUrl { get; set; } = "https://example.com/api";
    public string ApiKey { get; set; }
    public IEnumerable<int> Categories { get; set; }

    public NzbDroneValidationResult Validate() => /* … */;
}
```

The settings class is reflected over by `Mangarr.Http/ClientSchema/SchemaBuilder.cs` to render the UI form for adding/editing the indexer.

### Request Generator
```csharp
public class MyIndexerRequestGenerator : IIndexerRequestGenerator
{
    public IndexerPageableRequestChain GetRecentRequests() { /* RSS fetch */ }
    public IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria sc) { /* … */ }
    public IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria sc) { /* … */ }
    public IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria sc) { /* … */ }
    public IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria sc) { /* … */ }
    public IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria sc) { /* … */ }
}
```

### Parser
```csharp
public class MyIndexerParser : IParseIndexerResponse
{
    public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
    {
        // XML / JSON / HTML scraping → ReleaseInfo[]
    }
}
```

`ReleaseInfo` (returned objects) include `Title`, `DownloadUrl`, `Size`, `Indexer`, `IndexerId`, `PublishDate`, `InfoUrl`, `Guid`, etc. Torrent variants extend with `Seeders`, `Peers`, `Magnet`, `InfoHash`.

## RSS Sync Flow

```
Scheduler ticks (RssSyncCommand)
    ↓
RssSyncService.Sync()
    ↓
For each enabled indexer:
    ├─ FetchAndParseRssService.Fetch() → List<ReleaseInfo>
    └─ DecisionEngine.GetRssDecision() → grab approved releases
```

## Search Flow

```
EpisodeSearchCommand / SeasonSearchCommand / etc.
    ↓
NzbSearchService.SeriesSearch / EpisodeSearch / etc.
    ↓
Build SearchCriteriaBase (subclass per type)
    ↓
For each enabled indexer (parallel):
    └─ Indexer.GetReleases(criteria) → List<ReleaseInfo>
    ↓
Aggregate, parse, decide, sort, grab top
```

## SearchCriteria Hierarchy (`../IndexerSearch/Definitions/`)

| Class | When Used |
|-------|-----------|
| `SearchCriteriaBase` | abstract base — series + monitored info |
| `SingleEpisodeSearchCriteria` | One episode |
| `SeasonSearchCriteria` | Whole season |
| `DailyEpisodeSearchCriteria` | Date-based daily show |
| `AnimeEpisodeSearchCriteria` | Anime (absolute episode number) |
| `AnimeSeasonSearchCriteria` | Anime full season |
| `SpecialEpisodeSearchCriteria` | Specials (S00) |

For Mangarr, additional types will be needed: `ChapterSearchCriteria`, `VolumeSearchCriteria`, possibly `OneShotSearchCriteria`.

## Adding a New Indexer

1. Create folder `Indexers/MySite/`.
2. Add `MySiteSettings.cs` (implements `IIndexerSettings`, has its own validator).
3. Add `MySite.cs` (extends `HttpIndexerBase<MySiteSettings>`).
4. Add `MySiteRequestGenerator.cs` and `MySiteParser.cs`.
5. **No DI registration** — auto-discovered.
6. Add tests under `src/NzbDrone.Core.Test/IndexerTests/MySiteTests/`.
7. Optionally provide `DefaultDefinitions` in the provider class for preset configs.
8. The UI form is auto-generated from your settings class — use `[FieldDefinition]` attributes for form metadata.

## Manga Adaptation Plan

### Strategy
Most existing TV indexers (Newznab, Torznab, BroadcastheNet, HDBits, etc.) are **TV-specific** and not directly useful. Keep them around during transition; eventually mark TV-only indexers as deprecated for manga deployments.

### New Manga Indexers Needed
| Source Type | Examples |
|-------------|---------|
| Aggregator websites (HTML scrapers) | MangaDex, MangaSee, MangaPark, Mangakakalot |
| Manga torrent sites | Nyaa (already supported, just needs manga categories), AsianDrama, etc. |
| API-based services | MangaDex API (well-documented), MangaUpdates API |
| Direct from source | DynastyScans, manga publisher sites (offered/RAW) |

### Recommended First Implementations
1. **MangaDex** — official API, well-defined, massive catalog. Start here.
2. **Nyaa with manga filter** — already partially compatible.
3. **Generic manga RSS** — lowest-common-denominator scraper.

### Search Criteria Classes To Add
- `ChapterSearchCriteria { ChapterNumber, ScanlationGroupPreference, Language }`
- `VolumeSearchCriteria { VolumeNumber, ScanlationGroupPreference, Language }`
- `OneShotSearchCriteria { … }`

Update `IIndexerRequestGenerator` to accept them (alongside existing TV criteria).

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses `ReleaseInfo.Title`
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Consumes parsed releases
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Receives approved releases
- [../IndexerSearch/](../IndexerSearch/) — Search criteria definitions
- [../ThingiProvider/](../ThingiProvider/) — Provider plugin base

### Phase 17 — IComixSigner (RETIRED in Phase 39)

Phase 17 introduced `IComixSigner` (`Indexers/Comix/IComixSigner.cs`) as a runtime browser-driven signing seam for the in-process comix.to indexer (ported PuppeteerSharp → Microsoft.Playwright .NET in Phase 33.3). **The whole signer stack — `IComixSigner`, `ComixPlaywrightSigner`, `CassettingComixSigner`, `CassetteMode` — was DELETED in Phase 39 (Plan 39-03)** along with the in-process `ComixIndexer`. The external manga gateway now owns all embedded-browser + anti-bot clearance; Mangarr's Core runs no browser and no signer. The Phase 17/33.3 divergence rows in `DIVERGENCE.md` are preserved for provenance; the Phase 39 section records the retirement.
