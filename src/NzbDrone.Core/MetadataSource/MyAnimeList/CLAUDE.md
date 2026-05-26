# MetadataSource/MyAnimeList

## Purpose
v1 SECONDARY metadata source (D-16). Uses MAL v2 official API with client-ID-only auth per D-24 (NO OAuth). User pastes a free client ID from https://myanimelist.net/apiconfig.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MetadataSource\MyAnimeList`

## Key Files
| File | Purpose |
|------|---------|
| `MyAnimeListMetadataSource.cs` | Provider impl. `DefaultIsPrimary=false` (D-16). Maps `MalMangaResource` -> `Manga.Manga` (D-21 axes: PublicationYear from StartDate[..4], TotalChapterCount from NumChapters, PrimaryAuthor from Authors[].Role=="Story") |
| `MyAnimeListMetadataSourceSettings.cs` | `IHttpAggregatorSettings` impl + `ClientId` field with `PrivacyLevel.ApiKey` per D-24 |
| `MalApi.cs` | HTTP client. `/v2/manga` search + `/v2/manga/{id}`; `X-MAL-CLIENT-ID` header injected on every request via `ApplyHeaders` |
| `Resource/MalListEnvelope.cs` | Generic envelope `{data: [{node: T}]}` |
| `Resource/MalMangaResource.cs` | Manga DTO + AlternativeTitles + Authors + Genres |

## User Setup (per CONTEXT D-24)
1. Visit https://myanimelist.net/apiconfig
2. Create an app (free, no review required for read-only access)
3. Copy the Client ID
4. In Mangarr Settings → Metadata Sources → MyAnimeList, paste the Client ID
5. Click Test — provider validates by hitting `/v2/manga?q=test&limit=10&fields=...`

## Patterns / Conventions
- `X-MAL-CLIENT-ID` header injected via `MalApi.ApplyHeaders` on EVERY outbound request (D-24)
- `fields` query parameter is REQUIRED — MAL v2 returns sparse default response. Explicit field list in `MalApi.MangaFields`
- All requests carry `RateLimitKey = "myanimelist"` per Phase 1 (~60 req/min conservative budget per Phase 1 STACK research; ceiling ~100 req/min observed)
- `MangaTitleNormalizer.Normalize` applied to user-typed title before MAL query
- MAL exposes NO per-chapter feed; `GetMangaInfo` returns Manga + EMPTY chapter list. Synthesis fallback (D-17, Plan 02-09 ChapterListService) populates rows from `r.NumChapters` total-count
- Primary author extracted from `Authors[].Role == "Story"` per D-21
- `MalApi.GetById` sets `req.SuppressHttpError = true` so HTTP 404 maps cleanly to `MangaNotFoundException` instead of throwing the generic `HttpException` from `IHttpClient.Get<T>`. Mirrors `SkyHookProxy.GetSeriesInfo` precedent.

## Manga Adaptation Notes
- Kept separate from the `src/NzbDrone.Core/ImportLists/MyAnimeList/` vertical (the MAL ImportList uses an OAuth flow; D-24 explicitly chooses client-ID-only for the Phase 2 metadata source). The original Phase-2-era `MyAnimeListSettings.cs` filename the D-24 note referenced no longer exists at HEAD — the MAL ImportList was rebuilt in Phase 26/27 with `MalImportListSettings.cs` + `MalConstants.cs`.
- `ClientId` stored in `ProviderDefinition.Settings` JSON blob (plaintext-on-disk; same threat model as Mangarr indexer API keys per RESEARCH §Security Domain)

## Cross-References
- Scaffold: `src/NzbDrone.Core/MetadataSource/CLAUDE.md`
- Anime ImportList (OAuth, untouched): `src/NzbDrone.Core/ImportLists/MyAnimeList/`
- API docs: https://myanimelist.net/apiconfig/references/api/v2
