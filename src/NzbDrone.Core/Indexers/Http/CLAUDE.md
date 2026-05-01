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
