# MetadataSource/AniList

## Purpose
v1 SECONDARY metadata source (D-16). NEW file per D-25 — does NOT modify existing
`ImportLists/AniList/AniListAPI.cs` (anime ImportList).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MetadataSource\AniList`

## Key Files
| File | Purpose |
|------|---------|
| `AniListMetadataSource.cs` | Provider impl. `DefaultIsPrimary=false` (D-16 — secondary fallback) |
| `AniListMetadataSourceSettings.cs` | `IHttpAggregatorSettings` impl; `UserAgentOverride` exposed (no ToS like MangaDex) |
| `AniListMangaApi.cs` | Static GraphQL query strings (`type:MANGA` + all four title variants + staff edges) per D-25 |
| `Resource/AniListGraphQlResponse.cs` | Generic envelope `{ data: T }` plus `MediaResponseShape` / `PageResponseShape` |
| `Resource/AniListMedia.cs` | Media DTO + Title / StartDate / Staff / CoverImage sub-types |

## Patterns / Conventions
- All requests carry `RateLimitKey = "anilist"` per Phase 1 (30 req/min budget per STACK research)
- GraphQL queries are STATIC `const string`s (no string interpolation); all variables passed via
  the `variables` object — prevents GraphQL injection (T-INJ-03 mitigation)
- `MangaTitleNormalizer.Normalize` applied to user-typed title before AniList query
- AniList exposes NO per-chapter feed in v1; `GetMangaInfo` returns Manga + EMPTY chapter list.
  The synthesis fallback (D-17, Plan 02-09 ChapterListService) populates rows from
  `media.chapters` total-count when MangaDex is not cross-resolved
- 429 handler logs warning per RESEARCH §Pitfall 6; `IIndexerStatusService` integration deferred
  to Phase 3 indexer side
- Primary author extracted from `staff.edges` where `role == "Story"` per D-21

## Cross-Source Inputs (D-19 / D-21)
| AniList field           | Resolver consumer (Plan 02-09)                              |
|-------------------------|-------------------------------------------------------------|
| `media.idMal`           | Reverse-lookup MAL ID for D-19 path                         |
| `media.startDate.year`  | Publication-year axis (±1 gate)                             |
| `media.chapters`        | Total-chapter-count axis (within 10% gate)                  |
| `staff.edges[role=Story]` | Primary-author axis (exact match gate, D-21)              |

## Manga Adaptation Notes
- DO NOT modify `src/NzbDrone.Core/ImportLists/AniList/AniListAPI.cs` (anime ImportList, untouched per D-25)
- v2 manga ImportLists may extract a shared GraphQL transport base — out of Phase 2 scope per
  CONTEXT Deferred Ideas (REQUIREMENTS IMP-02 lands the v2 surface)

## Cross-References
- Scaffold: [`../CLAUDE.md`](../CLAUDE.md)
- Anime ImportList (untouched per D-25): [`../../ImportLists/AniList/`](../../ImportLists/AniList/)
- API docs: https://docs.anilist.co/guide/graphql/queries/media
- Rate-limit reference: [`../../Indexers/Http/CLAUDE.md`](../../Indexers/Http/CLAUDE.md)
