# MetadataSource/MangaDex

## Purpose

v1 PRIMARY metadata source per **D-16** — ships with `IsPrimary=true` by default. Implements `IProvideMangaInfo` + `ISearchForNewManga` against `api.mangadex.org`. Sibling provider to `MetadataSource/AniList/` and `MetadataSource/MyAnimeList/` (Plans 02-07 / 02-08).

Honest UA per Phase 1 D-13/D-14 + MangaDex ToS. **`UserAgentOverride` is NOT exposed in UI** — `MangaDexMetadataSourceSettings.UserAgentOverride` exists for the `IHttpAggregatorSettings` interface contract but carries NO `[FieldDefinition]`, so there is no surface for the user to spoof the honest `Mangarr/{version}` UA. This realizes the **T-CONFIG-DRIFT-01** mitigation by absence.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MetadataSource\MangaDex`

## Key Files

| File | Purpose |
|------|---------|
| `MangaDexMetadataSource.cs` | Provider impl. Extends `HttpMetadataSourceBase<MangaDexMetadataSourceSettings>`. `DefaultIsPrimary => true` per **D-16**. `MangaTitleNormalizer.Normalize` applied to user-typed search title before query. |
| `MangaDexMetadataSourceSettings.cs` | `IHttpAggregatorSettings` impl. NO `[FieldDefinition]` on `UserAgentOverride` per ToS. Defaults `BaseUrl = "https://api.mangadex.org"`, `SourceKey = "mangadex"`. |
| `MangaDexApi.cs` | HTTP client wrapper. `Search` (limit=10), `GetById` (throws `MangaNotFoundException` on 404), `GetFeed` (paginated `limit=500` per MangaDex contract). |
| `Resource/MangaListResource.cs` | List envelope DTO (data array). |
| `Resource/MangaResource.cs` | Single manga + `MangaAttributes`. **Pitfall 7**: `Links` is `Dictionary<string, string>` because MangaDex ships `al` / `mal` as JSON STRING values; resolver runs `int.TryParse` before persisting. |
| `Resource/ChapterFeedResource.cs` | Chapter feed envelope + entries. `Chapter` field is STRING per API; caller `decimal.TryParse` with InvariantCulture. |

## Patterns / Conventions

- All outbound requests carry `RateLimitKey = "mangadex"` per **D-22** (40 req/min budget shared with the Phase 3 indexer when it lands).
- `links.al` / `links.mal` parsed via `int.TryParse` per **Pitfall 7** (MangaDex stores them as STRING; legacy records may carry slugs that must be silently skipped, not exception).
- `MangaTitleNormalizer.Normalize` applied to user-typed title before search — single source of truth for canonicalization, shared with `CrossSourceIdResolver` (Plan 02-09).
- `MangaNotFoundException` thrown on upstream 404 from `GetById` (also wrapping non-Guid input strings as documented "not-a-MangaDex-id" failures).
- The `*ByAniListId` / `*ByMalId` reverse-lookup overloads return empty lists — MangaDex's API does not support cross-source ID reverse query directly. `CrossSourceIdResolver` (Plan 02-09) wires the Jaro-Winkler ≥0.85 + 2-of-3 multi-axis confirm fuzzy fallback per **D-19..D-22**.
- Cover-art handled by `MangaMediaCoverService` (Plan 02-09); this provider only extracts `MangaDataItem.Relationships` URLs/filenames.

## Manga Adaptation Notes

- Phase 3 will add a separate `MangaDexIndexer` for the chapter feed; both share `SourceKey = "mangadex"` for unified rate budget.
- Phase 8 cutover: file paths stay (no rename within `MangaDex/`); the only change is when `IProvideSeriesInfo` / `ISearchForNewSeries` / `SkyHookProxy` are deleted.
- D-21 multi-axis confirm inputs (`PublicationYear`, `PrimaryAuthor`, `TotalChapterCount`) are populated on the `Manga` model from `attrs.Year`, the `author` relationship, and `attrs.LastChapter` respectively.

## Cross-References

- Scaffold: `src/NzbDrone.Core/MetadataSource/CLAUDE.md`
- Phase 1 HTTP rate budget: `src/NzbDrone.Core/Indexers/Http/CLAUDE.md`
- Sonarr analog: `src/NzbDrone.Core/MetadataSource/SkyHook/CLAUDE.md`
- Title normalizer: `src/NzbDrone.Core/Parser/Manga/CLAUDE.md`
- API docs: https://api.mangadex.org/docs

---
*Last updated: 2026-05-02 (Plan 02-06 — provider + Settings + API wrapper landed).*
