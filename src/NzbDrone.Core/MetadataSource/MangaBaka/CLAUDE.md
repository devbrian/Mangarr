# MetadataSource/MangaBaka

## Purpose

v1.3 **DEFAULT PRIMARY** metadata source per **D-01a** (Phase 41) — ships with `DefaultIsPrimary=true`. Implements `IProvideMangaInfo` + `ISearchForNewManga` against `api.mangabaka.org`. NEW-in-Mangarr sibling provider to `MetadataSource/MangaDex/`, `MetadataSource/AniList/`, and `MetadataSource/MyAnimeList/` — there is no Sonarr analog (Sonarr's sole metadata source is TheTVDB/SkyHook).

MangaBaka was chosen as the new default primary because it ships **direct cross-source integer IDs** (`source.anilist.id` / `source.my_anime_list.id`) on the series record, which short-circuits the fuzzy `CrossSourceIdResolver` and the cross-title mis-attribution it risks (load-bearing for Phase-40 gateway grab attribution, **T-41-XID**).

MangaDex is **KEPT** (D-01) — its `DefaultIsPrimary` is simultaneously flipped to `false` (Plan 41-03) so a fresh DB seeds exactly ONE primary (MangaBaka). MangaDex remains a first-class fallback (a real per-chapter feed), still visible in the Settings Add picker; neither provider overrides the deprecation flag (D-02).

Honest UA per Phase 1 D-13/D-14. **`UserAgentOverride` is NOT exposed in UI** — `MangaBakaMetadataSourceSettings.UserAgentOverride` exists for the `IHttpAggregatorSettings` interface contract but carries NO `[FieldDefinition]`, so there is no UI surface to spoof the honest `Mangarr/{version}` UA. This realizes the **T-CONFIG-DRIFT-01** mitigation by absence.


## Key Files

| File | Purpose |
|------|---------|
| `MangaBakaMetadataSource.cs` | Provider impl. Extends `HttpMetadataSourceBase<MangaBakaMetadataSourceSettings>`. `DefaultSourceKey => "mangabaka"`, `DefaultIsPrimary => true` (D-01a). Synthesizes a `1..total_chapters` catalog (no per-chapter feed); reads direct cross-source ids; `MangaTitleNormalizer.NormalizeForSearch` applied to the search title. Lazy `Api` wrapper (ctor runs before `Definition` is assigned). |
| `MangaBakaMetadataSourceSettings.cs` | `IHttpAggregatorSettings` impl. NO `[FieldDefinition]` on `UserAgentOverride`. Defaults `BaseUrl = "https://api.mangabaka.org"`, `SourceKey = "mangabaka"` (D-10 — distinct budget from MangaDex's `"mangadex"`). |
| `MangaBakaApi.cs` | HTTP client wrapper. `Search` + `GetById` (returns null / throws `MangaNotFoundException` on 404 via `SuppressHttpError`). Stamps every outbound request with `RateLimitKey = SourceKey` + honest UA (injected `Func<string>`) + JSON Accept. |
| `Resource/MangaBakaSearchResource.cs` | Search-list envelope DTO. |
| `Resource/MangaBakaSeriesResource.cs` | Single by-id envelope DTO. |
| `Resource/MangaBakaSeries.cs` | Rich series record + `source` block mapping SEVEN cross-source ids onto `Manga` — AniList / MAL / Kitsu / AnimeNewsNetwork / Shikimori (RAW INTEGER `id` via `MangaBakaSourceRef`) and AnimePlanet / MangaUpdates (STRING `id` via `MangaBakaSourceStringRef`; slug / base36 token, never coerced to int) — plus `cover.raw.url` (pre-resolved poster URL), named title fields, `total_chapters` (JSON STRING). `links_v2[]` reading-platform links + per-source `rating` values remain intentionally NOT modelled (quick-260608-l2e). |

## Patterns / Conventions

- **Chapter synthesis (D-02 / D-04 / D-05 / D-06):** MangaBaka exposes only a `total_chapters` count, never a chapter listing. `GetMangaInfo` SYNTHESIZES a whole-number catalog `1..total_chapters`:
  - `null` / empty / `"0"` / non-integer `total_chapters` → empty list (D-04).
  - `MaxWholeCap = 5000` clamp + `_logger.Warn` before the loop (D-05 DoS guard — `total_chapters` is untrusted upstream). This replicates the `ChapterSynthesisService.MaxWholeCap` clamp precedent **but does NOT call `ChapterSynthesisService`** — it replicates only the clamp.
  - Each synthesized row carries a provenance `ExternalId` of the form **`mangabaka:{id}:c{n}`**, `Monitored = true`, null `Title`/`VolumeNumber`/`FirstReleaseDate`, `ChapterType.Regular` — mirroring `MangaDexMetadataSource.MapChapter` (the refresh path does NOT re-run `ChapterMonitoredService`).
- **Direct cross-source ids (D-03 / D-08-R; extended by quick-260608-l2e):** the `source` block maps SEVEN ids straight onto `Manga`, short-circuiting `CrossSourceIdResolver` entirely (T-41-XID):
  - **Int ids** (`MangaBakaSourceRef`, `int? Id`): `record.Source?.AniList?.Id` → `Manga.AniListId`, `MyAnimeList` → `MalId`, `Kitsu` → `KitsuId`, `AnimeNewsNetwork` → `AnimeNewsNetworkId`, `Shikimori` → `ShikimoriId`. Read via the `is int x` guard — no `int.TryParse` on a string (the MangaDex Pitfall 7 path).
  - **String ids** (`MangaBakaSourceStringRef`, `string Id`): `record.Source?.AnimePlanet?.Id` → `Manga.AnimePlanetId` (slug like `"solo-leveling"`), `MangaUpdates` → `MangaUpdatesId` (base36 token like `"6z1uqw7"`). Assigned only when not null-or-whitespace. NEVER coerced to int (a future int-typing would throw a JsonReaderException, pinned by `MangaBakaDeserializationFixture`).
  - The int-vs-string ref split is exactly `MangaBakaSourceRef` (int) vs `MangaBakaSourceStringRef` (string). All seven ids are **immutable post-add** (`Manga.ApplyChanges` omit) — parity with the canonical id convention.
  - `MangaDexId` stays **null** (D-03a): a MangaBaka-sourced manga is not a MangaDex record, so grab attribution leans on the AniList/MAL axes. Only `links_v2[]` reading-platform links + per-source `rating` values remain intentionally NOT modelled.
- **Rate budget (D-10):** all outbound requests carry `RateLimitKey = "mangabaka"`, isolated from MangaDex's `"mangadex"` budget. The documented MangaBaka budget is **30 req/min search, 120 req/min lookup**.
- **UA-by-absence:** `UserAgentOverride` carries NO `[FieldDefinition]` (T-CONFIG-DRIFT-01).
- **Capability gap (D-07):** `SearchForNewMangaByAniListId` / `SearchForNewMangaByMalId` return EMPTY lists — MangaBaka exposes no AniList/MAL reverse-lookup endpoint. This is a documented gap, NOT a swallowed failure (the resolver is unnecessary because the direct ids are already on the record).
- **`SearchForNewMangaByMangaDexId` reused as native int-by-id (RESEARCH Open Q1):** the interface slot name is legacy (minted for MangaDex's GUID-string id); for MangaBaka it is a provider-defined int-by-id fetch. The `ISearchForNewManga` interface is NOT widened (4-provider blast radius).
- **Demographic mapping (Phase 41 fix-forward):** MangaBaka ships the publication demographic INSIDE the flat `genres` array (e.g. `["action","adventure","shounen"]`) — there is NO dedicated `publicationDemographic` peer like MangaDex's. `MapDemographic(record.Genres)` scans the genres for the first demographic term and maps it to the `MangaDemographic` enum (accepts both `shounen`/`shonen` + `shoujo`/`shojo` romanizations; null when absent — the "not categorized" sentinel). This is load-bearing for `DemographicSpecification` auto-tagging on MangaBaka-sourced manga (parity with `MangaDexMetadataSource.MapDemographic`). Proven by `MangaBakaMetadataSourceFixture.GetMangaInfo_maps_demographic_from_genres` (+ null case). The demographic term is left IN `Genres` (not stripped) — minimal blast radius; MangaBaka itself classifies it as a genre.
- **`Test()` (D-09):** MangaBaka has no `/ping`; `Test()` runs a canned `Api.Search("test")`. No credential field exists, so no secret leaks in a failure message.
- Titles: `SelectPreferredTitle` prefers `title` → `romanized_title` → `native_title` → first secondary. `CollectAlternativeTitles` harvests every title string pre-normalized via `MangaTitleNormalizer.Normalize` (GH #118 analog of MangaDex's `CollectAlternativeTitles`). `titles[]` entries carry no title string (language + primary flag only) so they contribute nothing.

## Seeding Invariant (Phase 41 — Open Question 2 resolution)

The single-primary seed invariant is enforced across THREE paths (proven by `NzbDrone.Core.Test/MetadataSource/MangaBakaSeedFixture.cs`):

1. **Fresh DB** — only MangaBaka has `DefaultIsPrimary=true`, so the empty-table seed lands exactly one primary (MangaBaka).
2. **Upgraded DB, no primary** (the post-**Migration 012** demoted state — MangaDex present but non-primary, MangaBaka absent) — `MetadataSourceFactory.InitializeProviders`'s non-empty backfill branch creates a MangaBaka row and promotes it (no primary existed). MangaDex is KEPT, still non-primary.
3. **Upgraded DB, explicit non-MangaDex primary** — a user's explicit primary (e.g. AniList) is preserved (D-01 guard); MangaBaka backfills NON-primary.

In all three, `Count(IsPrimary) == 1`. **Migration 012** (`012_v1_3_add_mangabaka_metadata_source.cs`) demotes the MangaDex-as-primary row on upgrade but deliberately does NOT INSERT a MangaBaka primary row — the factory `Create(provider.DefaultDefinitions[...])` path is the single Settings-JSON serialization source-of-truth (option (b) factory backfill, not a fragile migration-INSERT).

## Manga Adaptation Notes

- NEW-in-Mangarr provider — no Sonarr analog (see `DIVERGENCE.md` Phase 41 entry).
- Chapter synthesis is the MangaBaka adaptation of the MangaDex `MapChapter` convention for a source that ships no per-chapter feed.
- `Manga.MangaBakaId` is `int?` (Plan 41-01), diverging from `Guid? MangaDexId` because MangaBaka ids are integers.

## Cross-References

- Scaffold: `src/NzbDrone.Core/MetadataSource/CLAUDE.md`
- Sibling primary it pairs with: `src/NzbDrone.Core/MetadataSource/MangaDex/CLAUDE.md` (DefaultIsPrimary flipped to false)
- Seed seam: `src/NzbDrone.Core/MetadataSource/MetadataSourceFactory.cs` (`InitializeProviders` backfill branch)
- Seed proof: `src/NzbDrone.Core.Test/MetadataSource/MangaBakaSeedFixture.cs`
- Migration: `src/NzbDrone.Core/Datastore/Migration/012_v1_3_add_mangabaka_metadata_source.cs`
- Provider fixtures: `src/NzbDrone.Core.Test/MetadataSource/MangaBaka/MangaBakaMetadataSourceFixture.cs` + `MangaBakaRateLimitKeyFixture.cs`
- Fork divergence: `DIVERGENCE.md` (Phase 41 — MangaBaka provider)

---
*Last updated: 2026-06-07 (Plan 41-05 — provider + Settings + API wrapper landed in Plan 41-03; this doc + the seed-invariant proof + DIVERGENCE entry close the phase doc obligation).*
*Last updated: 2026-06-08 (quick-260608-l2e — extended the `source` block from the original AniList/MAL-only mapping to SEVEN cross-source ids: added Kitsu/AnimeNewsNetwork/Shikimori (int via `MangaBakaSourceRef`) + AnimePlanet/MangaUpdates (string via new `MangaBakaSourceStringRef`); Migration 013 adds the 5 nullable columns; round-tripped through `MangaResource`; rendered as detail-page badges in `MangaDetailsLinks.tsx`. All seven ids immutable post-add (ApplyChanges omit). `links_v2[]` + per-source ratings still NOT modelled.)*
