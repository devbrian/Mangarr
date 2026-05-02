# NzbDrone.Core/Indexers/Http

## Purpose

Manga aggregator indexer base class — `HttpAggregatorBase<TSettings>` extends `HttpIndexerBase<TSettings>` to add:
- Per-`SourceKey` shared rate-limit budget across indexer + downloader (D-11/D-12)
- Honest-by-default User-Agent (`Mangarr/{version}`) with per-instance override (D-13/D-14)
- Manga-shaped abstract methods (Phase 3 source plugins extend)

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\Http\`

## Key Files

| File | Purpose |
|------|---------|
| `HttpAggregatorBase.cs` | Abstract base class; Phase 3 sources extend |
| `HttpAggregatorSettingsBase.cs` | `IHttpAggregatorSettings` interface + reusable Settings POCO with FieldDefinitions for SourceKey / Rate / UserAgentOverride |

## Patterns / Conventions

All concrete subclasses must:
- Override `DefaultSourceKey` to return the source's logical name (e.g., `"mangadex"`)
- Preserve the 6-arg constructor signature for ThingiProvider auto-discovery
- Set `public override DownloadProtocol Protocol => DownloadProtocol.Http;` (Phase 1 enum addition)

## Manga Adaptation Notes

Phase 1 ships only the abstract base class + Settings interface. Phase 3 adds concrete `MangaDexIndexer`, `ComixToIndexer`, `MangaFireIndexer` deriving from this base.

The honest-UA opt-out (D-14) is policy-default-on, not a hard lock. Source plugins may surface or hide the override field per their ToS posture (MangaDex MUST hide it; Cloudflare-protected aggregators MAY expose it).

## Cross-References

- [HttpIndexerBase parent](../HttpIndexerBase.cs)
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — Phase 1 entries
- [Phase 1 PATTERNS](../../../../.planning/phases/01-foundation/01-PATTERNS.md)
- [Phase 1 RESEARCH](../../../../.planning/phases/01-foundation/01-RESEARCH.md)

## Phase 3 — Manga Aggregator Usage

Phase 3 source plugins (`MangaDex`, `Comix`) extend `HttpAggregatorBase<TSettings>` and:

1. **MUST implement** the manga overloads `Fetch(MangaSearchCriteria)` and `Fetch(ChapterSearchCriteria)` (abstract; D-02).
2. **MUST throw `NotSupportedException`** for the 7 inherited TV `Fetch(SeasonSearchCriteria)` etc. overloads (D-03 — per-class stubs; not pushed into the base).
3. **MAY override `GetDownloadHeaders(ReleaseInfo)`** to return per-source `Referer`/`Origin` headers consumed by Phase 4's in-process downloader (D-14). Default empty.
4. **Inherit for free** the per-`SourceKey` rate budget (Phase 1 D-11/D-12) and honest-by-default `Mangarr/{version}` UA (Phase 1 D-13/D-14).

### MangaDex ToS posture (CRITICAL)

`MangaDexIndexerSettings.UserAgentOverride` MUST omit `[FieldDefinition]` to prevent UI exposure of the override field — MangaDex ToS requires the honest UA. See `Indexers/MangaDex/CLAUDE.md` and `MangaDexIndexerSettingsHonestUaFixture` (compile-time reflection assertion enforces this; T-CONFIG-DRIFT-01 mitigation by absence).

### Source plugin file layout

Sibling to existing TV `Indexers/Newznab/`, `Indexers/Nyaa/`:

```
src/NzbDrone.Core/Indexers/MangaDex/   # Phase 3
src/NzbDrone.Core/Indexers/Comix/       # Phase 3
```

(MangaFire descoped to v2 per Phase 3 D-19 — RESEARCH.md Q-1 resolution; chapter-list path requires WebView VRF token extraction which conflicts with D-10 + D-13. Reopens when v2 SOLVE-01 ships.)

### Phase 4 / Phase 5 / Phase 6 / Phase 8 deferrals

- Phase 4: in-process downloader consumes `ReleaseInfo.DownloadUrl` (chapter-manifest URL) + `GetDownloadHeaders(ReleaseInfo)` per-image GET headers
- Phase 5: TranslationProfile ordinal gate consumes `ReleaseInfo.TranslatedLanguage` (advisory `MangaSearchCriteria.PreferredLanguages` is bandwidth savings only — D-07)
- Phase 6: `MangaSearchCommand` / `ChapterSearchCommand` construct criteria + dispatch through `ReleaseSearchService`
- Phase 8: Delete TV-shaped overloads with `Tv/`; collapse `MangaSearchCriteriaBase` into the renamed `SearchCriteriaBase`
