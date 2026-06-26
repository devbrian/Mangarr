# NzbDrone.Core/Indexers

## Purpose

**Indexer plugins** — services that fetch lists of releases from external sites, for both periodic RSS sync ("what's new") and on-demand search. Indexers follow the **ThingiProvider** plugin pattern.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\`

> **Phase 39 (Plan 39-03/04) — in-process site-scraper indexers RETIRED.** Mangarr no longer
> runs in-process manga aggregator scrapers. `Indexers/Gateway/GatewayIndexer.cs` (Phase 37) is
> the **sole `IIndexer`** — it fans search/recent requests out to the external manga gateway,
> which owns the embedded browser + anti-bot clearance; **Mangarr ships zero embedded browser.**
> Deleted in Phase 39: the `MangaDex/` + `Comix/` in-process indexer dirs; the Comix anti-bot
> signer stack (`ComixPlaywrightSigner`/`CassettingComixSigner`/`IComixSigner`/`CassetteMode`);
> the `IHttpAggregator` page-fetch marker + `HttpAggregatorBase` + `ChapterManifest`/`ChapterPage`/
> `ManifestExpiredException`; and the `Cloudflare/` clearance cascade. `Microsoft.Playwright`
> was dropped from `Mangarr.Core`. **`IHttpAggregatorSettings` SURVIVES** in `Http/HttpAggregatorSettingsBase.cs`
> — the per-`SourceKey` settings interface the 3 metadata sources implement for CF scoring,
> distinct from the deleted `IHttpAggregator` marker (the "named-gate" carve). `IndexerFactory`
> seeds only `GatewayIndexer`. There are **no** Sonarr Usenet/Torrent indexer directories on disk
> (the Newznab/Torznab/Nyaa/etc. verticals were never carried into the manga baseline). See
> `DIVERGENCE.md` Phase 39 section.

## Top-Level Files (verified at HEAD)

| File | Purpose |
|------|---------|
| `IIndexer.cs` | Provider interface (`Fetch` / `FetchRecent`) |
| `IndexerBase.cs` | Common base implementation |
| `HttpIndexerBase.cs` | Generic HTTP-based indexer + the multi-page paging engine `GatewayIndexer` reuses (`PageSize` driven) |
| `IndexerFactory.cs` / `IndexerRepository.cs` | ThingiProvider factory (seeds `GatewayIndexer` only) + persistence |
| `IndexerDefinition.cs` | Persisted provider config (`SyncInterval`, `LastRssSync`, enable toggles) |
| `IIndexerRequestGenerator.cs` / `IndexerPageableRequest.cs` / `IndexerPageableRequestChain.cs` | Request-building strategy + multi-page orchestration |
| `IProcessIndexerResponse.cs` / `IndexerRequest.cs` / `IndexerResponse.cs` | Response parse contract + request/response DTOs |
| `IIndexerSettings.cs` | Settings marker (reflected over by `Mangarr.Http/ClientSchema` to render the UI form) |
| `IndexerStatus*.cs` / `IIndexerSourceStatus*.cs` / `IndexerSourceStatus*.cs` | Health/escalation ladder — per-indexer failure state + per-`SourceKey` source status (gateway-written) |
| `CachedIndexerSettingsProvider.cs` | Cached settings lookup |
| `DownloadProtocol.cs` | enum `{ Unknown = 0, Http = 3 }` — the gateway is `DownloadProtocol.Http` |

## Subdirectories (verified at HEAD)

| Folder | Purpose |
|--------|---------|
| `Gateway/` | `GatewayIndexer` + `GatewayParser` + `GatewayRequestGenerator` + `GatewaySettings` + `GatewayCapabilities`/`GatewayCapabilitiesProvider` + `Responses/` — the **sole `IIndexer`** (Phase 37; external manga-gateway client). **SEARCH is multi-page** (offset validated live 2026-06-20): offset stride (`0, L, 2L, …`, `L = ResultLimit`) walked by the kept `HttpIndexerBase` paging engine via a dynamic `PageSize => GatewayRequestGenerator.ResolveEffectiveLimit(...)`, stopping at the first short page. **Paging splits on `Interactive`:** automatic search (RSS/missing/monitored) walks FULL coverage (bounded by `MaxSearchPages`), while the interactive Search tab emits a SINGLE first page. `/recent` is single-request. |
| `Http/` | Holds the SURVIVING `IHttpAggregatorSettings` per-`SourceKey` settings interface (`HttpAggregatorSettingsBase.cs`) — see below. The `IHttpAggregator` page-fetch marker + `HttpAggregatorBase<TSettings>` were deleted in Phase 39 Plan 39-04. |
| `Exceptions/` | `ApiKeyException`, `IndexerException`, `RequestLimitReachedException`, `SizeParsingException`, `UnsupportedFeedException` |

### `Http/` — surviving settings interface (post-Phase-39)

`Http/HttpAggregatorSettingsBase.cs` defines **`IHttpAggregatorSettings`** — the per-`SourceKey`
settings interface implemented by the 3 metadata-source settings classes (MangaDex / AniList /
MyAnimeList) via `HttpMetadataSourceBase<TSettings>` and read by `MangaDownloadDecisionMaker` for
Custom-Format scoring. It survives the Phase-39 marker delete (the named-gate dual-grep proved no
break). The dead concrete `HttpAggregatorSettings` POCO + validator were removed with the indexers.
`GatewayIndexer` does NOT extend any `HttpAggregatorBase`; it implements the indexer contract directly.

## GatewayIndexer anatomy

`GatewayIndexer : HttpIndexerBase<GatewaySettings>` with `Protocol => DownloadProtocol.Http`. It pairs
a `GatewayRequestGenerator` (builds `/search` + `/recent` requests, owns the offset-paging math) with a
`GatewayParser` (maps the gateway JSON into `ReleaseInfo`, minting the opaque download handle at
`DownloadUrl` that `GatewayDownloadClient` later submits). `ReleaseInfo` carries `Title`, `DownloadUrl`,
`Indexer`, `IndexerId`, `PublishDate`, `Guid`, plus the manga axes (`TranslatedLanguage`, `ScanlationGroup`).

## Adding a New Indexer

The gateway is the sole manga indexer by design — Mangarr drives the external gateway, which owns the
browser + scraping. Do **not** add in-process site scrapers (that vertical was retired in Phase 39).
A new indexer is still auto-discovered by the ThingiProvider scan (no DI registration); the UI form is
generated from the settings class via `[FieldDefinition]` attributes.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses `ReleaseInfo.Title`
- [../DecisionEngine/Manga/CLAUDE.md](../DecisionEngine/Manga/CLAUDE.md) — Consumes parsed releases
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Receives approved releases (`GatewayDownloadClient`)
- [../IndexerSearch/Manga/CLAUDE.md](../IndexerSearch/Manga/CLAUDE.md) — Search dispatch + fan-out
- [../ThingiProvider/](../ThingiProvider/) — Provider plugin base

### Phase 17 — IComixSigner (RETIRED in Phase 39)

Phase 17 introduced `IComixSigner` as a runtime browser-driven signing seam for the in-process comix.to
indexer (ported PuppeteerSharp → Microsoft.Playwright .NET in Phase 33.3). **The whole signer stack —
`IComixSigner`, `ComixPlaywrightSigner`, `CassettingComixSigner`, `CassetteMode` — was DELETED in Phase 39
(Plan 39-03)** along with the in-process `ComixIndexer`. The external manga gateway now owns all
embedded-browser + anti-bot clearance; Mangarr's Core runs no browser and no signer. The Phase 17/33.3
divergence rows in `DIVERGENCE.md` are preserved for provenance; the Phase 39 section records the retirement.
