# NzbDrone.Core/MetadataSource

## Purpose

External metadata provider integrations — fetch manga/chapter info from third-party APIs. This is a **ThingiProvider family** (`IMetadataSource`): providers are auto-discovered by reflection and exactly one is flagged primary.

Sonarr's sole TV metadata source (TheTVDB via the Mangarr-hosted **SkyHook** proxy) was **deleted in Phase 15** — `SkyHook/`, `IProvideSeriesInfo`, `ISearchForNewSeries`, `SkyHookProxy` no longer exist. The manga peers are the four providers below; **MangaBaka** is the v1.3 default primary (Phase 41).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MetadataSource\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `IProvideMangaInfo.cs` | Interface — `GetMangaInfo(string sourceId)` → `(Manga, List<Chapter>)` (D-14) |
| `ISearchForNewManga.cs` | Interface — search by title or by cross-source ID (D-14) |
| `IMetadataSource.cs` | Composite interface; required for ThingiProvider auto-discovery (Pitfall 4) |
| `MetadataSourceBase.cs` | Abstract base implementing the IProvider members |
| `HttpMetadataSourceBase.cs` | HTTP-backed base. SIBLING (not subclass) of the former `HttpAggregatorBase` (deleted Phase 39) — duplicates SourceKey + UA injection to keep the `IIndexer` / `IMetadataSource` registries cleanly separate (RESEARCH §Open Question 1) |
| `MetadataSourceDefinition.cs` | ProviderDefinition with `IsPrimary` bool (D-15) |
| `IMetadataSourceFactory.cs` / `MetadataSourceFactory.cs` | ProviderFactory + `SetPrimary` at-most-one invariant (D-15); `InitializeProviders` seed/backfill |
| `IMetadataSourceRepository.cs` / `MetadataSourceRepository.cs` | ProviderRepository<MetadataSourceDefinition> with `FindByName` + `GetPrimary` |
| `MangaNotFoundException.cs` | Thrown by providers on upstream 404 |
| `CrossSourceIdResolver.cs` | Jaro-Winkler ≥0.85 + 2-of-3 multi-axis confirm fuzzy cross-source ID match (D-19..D-22, Plan 02-09) |
| `SearchMangaComparer.cs` | Ranks/orders search results for the add-UX list |

## Subdirectories (provider implementations)

| Dir | Provider | Default | Notes |
|-----|----------|---------|-------|
| `MangaBaka/` | `MangaBakaMetadataSource` | `DefaultIsPrimary=true` | v1.3 DEFAULT PRIMARY (Phase 41); direct cross-source ids + synthesized `1..total_chapters` catalog. See [MangaBaka/CLAUDE.md](./MangaBaka/CLAUDE.md). |
| `MangaDex/` | `MangaDexMetadataSource` | `DefaultIsPrimary=false` (flipped Phase 41) | First-class fallback — the one source with a real per-chapter feed. See [MangaDex/CLAUDE.md](./MangaDex/CLAUDE.md). |
| `AniList/` | `AniListMetadataSource` | `DefaultIsPrimary=false` | GraphQL secondary. See [AniList/CLAUDE.md](./AniList/CLAUDE.md). |
| `MyAnimeList/` | `MyAnimeListMetadataSource` | `DefaultIsPrimary=false` | MAL v2, client-ID-only auth. See [MyAnimeList/CLAUDE.md](./MyAnimeList/CLAUDE.md). |

Migration 005 deprecated AniList/MAL as default candidates; Migration 012/013 added MangaBaka. All four remain selectable in Settings → Metadata Sources.

## How It's Used

```
ISearchForNewManga.SearchForNewManga("title")  → List<Manga>  (UI add list)
   ↓ user picks one → AddMangaService persists
RefreshMangaService.RefreshMangaInfo(mangaId)
   ↓ IProvideMangaInfo.GetMangaInfo(sourceId) → (Manga, List<Chapter>)
   ↓ reconcile fetched chapters with DB (insert new / update changed / delete removed)
```

`RefreshMangaService` dispatches to the configured primary source and can relink a stale primary id on a 404 (see `../Manga/RefreshMangaService.cs`).

## IMetadataSource ThingiProvider family (Phase 2)

Phase 2 introduced this pluggable provider type per D-14, replacing the Sonarr concrete-singleton `IProvideSeriesInfo`/`ISearchForNewSeries` (SkyHookProxy), which was deleted in the Phase 15 cutover.

### IsPrimary Invariant (D-15)

At most ONE row in the `MetadataSources` table may have `IsPrimary=true`. The DB
allows the invariant to be broken (Migration 002 has no DB-level constraint); the factory
restores it on every `SetPrimary(id)` call: demote ALL, then promote target, transactional
via `Update(IEnumerable)` → `IProviderRepository.UpdateMany`. Wave 0
`MetadataSourceFactoryFixture.SetPrimary_demotes_prior_primary_when_promoting_another`
verifies (mitigates threat T-CONFIG-DRIFT-01).

### Why HttpMetadataSourceBase Is a Sibling

The former `HttpAggregatorBase : HttpIndexerBase : IndexerBase` chain meant extending it
would register metadata sources as `IIndexer` via DryIoc auto-discovery. We want the two
ThingiProvider families separate, so per RESEARCH §Open Question 1, `HttpMetadataSourceBase`
duplicates ~30 lines of SourceKey + UA injection rather than subclassing — intentional cost
to keep the registries clean. (`HttpAggregatorBase` itself was deleted in Phase 39; this base
survives independently — see the class comment in `HttpMetadataSourceBase.cs`.)

### SkyHook cutover (done — Phase 15)

`IProvideSeriesInfo`, `ISearchForNewSeries`, and `SkyHookProxy` (the TV side) were deleted in Phase 15. `IProvideMangaInfo` / `ISearchForNewManga` (already manga-named — no rename needed) are the sole metadata contracts at HEAD.

## Phase 41 Additions (MangaBaka default-primary + seeding backfill)

Phase 41 adds the **MangaBaka** metadata source as the new v1.3 DEFAULT PRIMARY (D-01a) and resolves the seed-vs-migration gap (RESEARCH Open Question 2). NEW-in-Mangarr — no Sonarr analog. See [MangaBaka/CLAUDE.md](./MangaBaka/CLAUDE.md) and `DIVERGENCE.md` (Phase 41).

### DefaultIsPrimary flip (Plan 41-03)

- `MangaBakaMetadataSource.DefaultIsPrimary => true` (D-01a — new default primary).
- `MangaDexMetadataSource.DefaultIsPrimary => false` (paired flip) so a fresh DB seeds exactly ONE primary. MangaDex is KEPT as a first-class fallback (D-01 / D-02), not deprecated.

### Migration 012 — guarded demote (Plan 41-01)

`012_v1_3_add_mangabaka_metadata_source.cs` (`[Migration(12)]`, head was 011) adds `Manga.MangaBakaId` and DEMOTES only the MangaDex-as-primary row on upgrade (D-01 guard; cross-dialect-safe typed `DbType.Boolean` params, `MIN("Id")` single-row scope). It deliberately does **NOT** INSERT a MangaBaka primary row — replicating the provider's Settings-JSON serialization in raw SQL is fragile.

### `InitializeProviders` backfill (Plan 41-01)

`MetadataSourceFactory.InitializeProviders` no longer unconditionally short-circuits on a non-empty table. A non-empty (upgrade) backfill branch creates any primary-default provider lacking a row, promoting it to primary ONLY when no primary currently exists:

- **Common upgrade path:** Migration 012 demoted MangaDex → no primary exists → MangaBaka is promoted.
- **Explicit non-MangaDex primary:** a primary still exists → MangaBaka backfills NON-primary, preserving the user's choice.

This preserves the at-most-one-primary invariant (D-15) on BOTH fresh and upgraded DBs. Proven by `NzbDrone.Core.Test/MetadataSource/MangaBakaSeedFixture.cs` (fresh / upgraded-no-primary / upgraded-explicit-primary; each asserts `Count(IsPrimary) == 1`).

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [MangaBaka/CLAUDE.md](./MangaBaka/CLAUDE.md) — MangaBaka default-primary provider (Phase 41)
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga/Chapter populated from this (the Sonarr `Tv/` analog was deleted in Phase 15)
- [../Manga/RefreshMangaService.cs](../Manga/RefreshMangaService.cs) — Caller (replaced the deleted Sonarr `Tv/RefreshSeriesService.cs`)
- [../../Mangarr.Api.V5/Manga/MangaLookupController.cs](../../Mangarr.Api.V5/Manga/MangaLookupController.cs) — Search-add UX entrypoint (replaced the deleted Sonarr `Series/SeriesLookupController.cs`)
- [../../../frontend/src/AddManga/CLAUDE.md](../../../frontend/src/AddManga/CLAUDE.md) — Frontend "Add" flow (Sonarr `AddSeries/` renamed in Phase 17.3)
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Manga / Chapter domain models consumed by `IProvideMangaInfo` / `ISearchForNewManga`
- [../Indexers/Http/HttpAggregatorSettingsBase.cs](../Indexers/Http/HttpAggregatorSettingsBase.cs) — home of `IHttpAggregatorSettings` (the `HttpAggregatorBase` class itself was deleted in Phase 39; `HttpMetadataSourceBase` survives as its sibling)
- [../ThingiProvider/](../ThingiProvider/) — ProviderBase / ProviderDefinition / ProviderFactory / ProviderRepository
