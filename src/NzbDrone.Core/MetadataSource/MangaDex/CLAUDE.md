# MetadataSource/MangaDex

## Purpose

First-class **fallback** metadata source — the one source with a real per-chapter feed. Shipped as the v1 default primary per **D-16**, but Phase 41 (Plan 41-03, D-01a) **flipped `DefaultIsPrimary` to `false`** so a fresh DB seeds MangaBaka as the sole primary; MangaDex is **KEPT** (D-01), still selectable and still the chapter-feed source. Implements `IProvideMangaInfo` + `ISearchForNewManga` against `api.mangadex.org`. Sibling provider to `MetadataSource/MangaBaka/`, `MetadataSource/AniList/`, and `MetadataSource/MyAnimeList/`.

Honest UA per Phase 1 D-13/D-14 + MangaDex ToS. **`UserAgentOverride` is NOT exposed in UI** — `MangaDexMetadataSourceSettings.UserAgentOverride` exists for the `IHttpAggregatorSettings` interface contract but carries NO `[FieldDefinition]`, so there is no surface for the user to spoof the honest `Mangarr/{version}` UA. This realizes the **T-CONFIG-DRIFT-01** mitigation by absence.


## Key Files

| File | Purpose |
|------|---------|
| `MangaDexMetadataSource.cs` | Provider impl. Extends `HttpMetadataSourceBase<MangaDexMetadataSourceSettings>`. `DefaultIsPrimary => false` (was `true` per D-16; flipped Phase 41 / Plan 41-03 D-01a — MangaBaka is the new default primary). `MangaTitleNormalizer.NormalizeForSearch` applied to user-typed search title before query. |
| `MangaDexMetadataSourceSettings.cs` | `IHttpAggregatorSettings` impl. NO `[FieldDefinition]` on `UserAgentOverride` per ToS. Defaults `BaseUrl = "https://api.mangadex.org"`, `SourceKey = "mangadex"`. |
| `MangaDexApi.cs` | HTTP client wrapper. `Search` (limit=10), `GetById` (throws `MangaNotFoundException` on 404), `GetFeed` (paginated `limit=500` per MangaDex contract). |
| `Resource/MangaListResource.cs` | List envelope DTO (data array). |
| `Resource/MangaResource.cs` | Single manga + `MangaAttributes`. **Pitfall 7**: `Links` is `Dictionary<string, string>` because MangaDex ships `al` / `mal` as JSON STRING values; resolver runs `int.TryParse` before persisting. |
| `Resource/ChapterFeedResource.cs` | Chapter feed envelope + entries. `Chapter` field is STRING per API; caller `decimal.TryParse` with InvariantCulture. |

## Patterns / Conventions

- All outbound requests carry `RateLimitKey = "mangadex"` per **D-22** (40 req/min budget; the Phase 3 in-process `MangaDexIndexer` that shared this budget was retired in Phase 39).
- `links.al` / `links.mal` parsed via `int.TryParse` per **Pitfall 7** (MangaDex stores them as STRING; legacy records may carry slugs that must be silently skipped, not exception).
- `MangaTitleNormalizer.NormalizeForSearch` applied to user-typed title before search — single source of truth for canonicalization, shared with `CrossSourceIdResolver` (Plan 02-09).
- `MangaNotFoundException` thrown on upstream 404 from `GetById` (also wrapping non-Guid input strings as documented "not-a-MangaDex-id" failures).
- The `*ByAniListId` / `*ByMalId` reverse-lookup overloads return empty lists — MangaDex's API does not support cross-source ID reverse query directly. `CrossSourceIdResolver` (Plan 02-09) wires the Jaro-Winkler ≥0.85 + 2-of-3 multi-axis confirm fuzzy fallback per **D-19..D-22**.
- Cover-art handled by `MangaMediaCoverService` (Plan 02-09); this provider only extracts `MangaDataItem.Relationships` URLs/filenames.

## Manga Adaptation Notes

- Phase 3 added a separate in-process `MangaDexIndexer` for the chapter feed (shared `SourceKey = "mangadex"`); it was **RETIRED in Phase 39** (Plan 39-03) when the external `GatewayIndexer` became the sole `IIndexer`. This metadata source is unaffected — it survives as a chapter-feed-bearing metadata provider.
- The Sonarr `IProvideSeriesInfo` / `ISearchForNewSeries` / `SkyHookProxy` cutover is done (deleted Phase 15); no rename happened within `MangaDex/`.
- D-21 multi-axis confirm inputs (`PublicationYear`, `PrimaryAuthor`, `TotalChapterCount`) are populated on the `Manga` model from `attrs.Year`, the `author` relationship, and `attrs.LastChapter` respectively.

## Cross-References

- Scaffold: `src/NzbDrone.Core/MetadataSource/CLAUDE.md`
- Phase 1 HTTP rate budget: `src/NzbDrone.Core/Indexers/CLAUDE.md` (the surviving `IHttpAggregatorSettings` rate-budget section; `Indexers/Http/HttpAggregatorSettingsBase.cs` is the home class)
- Sonarr analog (the TVDB/SkyHook metadata source this MangaDex source replaced): `src/NzbDrone.Core/MetadataSource/SkyHook/` — DELETED in Phase 15; cited for provenance only, absent at HEAD.
- Title normalizer: `src/NzbDrone.Core/Parser/Manga/CLAUDE.md`
- API docs: https://api.mangadex.org/docs

---
*Last updated: 2026-05-02 (Plan 02-06 — provider + Settings + API wrapper landed).*
