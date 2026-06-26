# NzbDrone.Core/IndexerSearch

## Purpose

The **search-and-dispatch surface** between the user/scheduled-task and the indexer fan-out. At HEAD
the only live content is the `Manga/` sibling (search commands + `IExecute<T>` dispatchers + RSS sync)
and the `Definitions/` manga `SearchCriteria` hierarchy. The Sonarr TV command/service family
(`SeriesSearchCommand`, `EpisodeSearchService`, `ReleaseSearchService`, etc.) was deleted in the
Phase 15 `Tv/` cutover — there are **no top-level `.cs` files left in this directory**.


## Subdirectories (verified at HEAD)

### `Definitions/`
| File | Purpose |
|------|---------|
| `MangaSearchCriteriaBase.cs` / `MangaSearchCriteria.cs` / `ChapterSearchCriteria.cs` | Phase 3 D-06 manga search-criteria hierarchy. `ChapterNumber` is `decimal` per Phase 2 D-12 widen. `PreferredLanguages` is advisory only (Phase 3 D-07; Phase 5 TranslationProfile authoritative). The TV `SearchCriteriaBase` + `EpisodeSearchCriteria`/`SeasonSearchCriteria`/etc. were deleted with `Tv/`. |

### `Manga/`
See [Manga/CLAUDE.md](./Manga/CLAUDE.md). The four search command POCOs + their `IExecute<T>`
services + `MangaReleaseSearchService` fan-out + `MangaRssSyncService` (summarized below).

## Phase 6 Manga Sibling

Phase 6 Plan 06-06 shipped `IndexerSearch/Manga/`. The `Protocol == DownloadProtocol.Http` fan-out
filter remains in `MangaReleaseSearchService`/`MangaRssSyncService` — it correctly selects the
gateway (`DownloadProtocol.Http`). The original "co-exist with the TV fan-out" rationale is moot
(TV deleted in Phase 15); the filter is now a harmless protocol guard.

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`IndexerSearch/Manga/`](./Manga/CLAUDE.md) | Plan 06-06 | Manga search commands + RSS sync + dispatchers. **Commands**: `MangaSearchCommand` (D-06 bulk-MangaIds dispatch — NOT per-chapter fan-out — fired by `MissingChapterSearchService` and Add-Manga search-on-add), `ChapterSearchCommand` (D-12 single-element list shape — used by Plan 06-08 auto-retry + Plan 06-09 Interactive Search modal Grab POST), `MissingChapterSearchCommand` (D-09 Wanted sweep — `int? MangaId` null = all monitored), `MangaRssSyncCommand` (D-07 payload-less scheduled poll trigger). **Services**: `MangaReleaseSearchService` (manga-shape `Task<List<MangaDownloadDecision>>` fan-out — filters `IIndexerFactory.AutomaticSearchEnabled()` to `Protocol == DownloadProtocol.Http`, parallel-fans `Fetch(MangaSearchCriteria) / Fetch(ChapterSearchCriteria)` with per-indexer try/catch, runs Phase 5 `IMakeMangaDownloadDecision.GetSearchDecision`); `MangaSearchService` + `ChapterSearchService` per-command dispatchers; `MissingChapterSearchService` walker (groups by MangaId per D-09 — NOT per-chapter fan-out — pushes ONE `MangaSearchCommand` per affected Manga); `MangaRssSyncService` (filters RSS-enabled indexers to `Protocol == DownloadProtocol.Http`, honors per-`IndexerDefinition.SyncInterval > 0` override on top of global `Config.MangaRssSyncInterval`). **D-04 GUARD**: NO `IsSynthetic` filter anywhere — synthetic rows (Phase 2 D-17 metadata-only-count fallback) are treated identically to real-feed chapters. **BL-01 GUARD**: queue dedup uses `IMangaQueueService` (Plan 06-05), NOT TV's `IQueueService`. **Anti-pattern C compliance** (sonarr-consistency-audit SKILL.md): `MangaRssSyncCommand` + `MissingChapterSearchCommand` are registered at runtime in `TaskManager.defaultTasks` — NOT seeded via 001 `Insert.IntoTable`. (The TV `EpisodeSearchService`/`ReleaseSearchService`/`FetchAndParseRssService` peers were deleted in Phase 15; there is nothing left to collapse with.) |
| `IndexerSearch/Definitions/MangaSearchCriteriaBase.cs` Manga/Chapter fully-qualified | Plan 06-06 | Namespace-collision fix: the new `NzbDrone.Core.IndexerSearch.Manga` namespace shadowed the bare `Manga.` prefix; the two `Manga.Manga / Manga.Chapter` references are now fully-qualified via the global root (`NzbDrone.Core.Manga.Manga / NzbDrone.Core.Manga.Chapter`). |

## Phase 6 D-04 + BL-01 Discipline

Two invariants to enforce when modifying any manga-side search code:

1. **D-04 IsSynthetic-treated-identically**: search/Wanted code paths must NOT add a `.Where(c => !c.IsSynthetic)` filter. Synthetic rows surface alongside real-feed rows. Plan 06-06 ships an explicit test asserting ONE `MangaSearchCommand` for a manga with mixed synthetic+real monitored chapters.
2. **BL-01 GUARD**: queue dedup uses `IMangaQueueService` (Plan 06-05 sibling), NEVER TV's `IQueueService`. The two services serve different rows.

## Manga Adaptation Notes

The fan-out + parallel-fetch + per-indexer try/catch pattern was ported from Sonarr's
`EpisodeSearchService` / `FetchAndParseRssService` (both since deleted with `Tv/` in Phase 15). The
two divergences that remain in the live manga services:
1. **Manga-shape return type** (`Task<List<MangaDownloadDecision>>`) — the deleted TV `IProcessDownloadDecisions` could not consume it.
2. **`Protocol == DownloadProtocol.Http` filter** on `IIndexerFactory.RssEnabled() / .AutomaticSearchEnabled()` — now a harmless protocol guard (it selects the gateway; the TV fan-out it once disambiguated from is gone).

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — `IIndexer.Fetch(SearchCriteria)` is the dispatch target
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `IMakeMangaDownloadDecision.GetSearchDecision` (Phase 5) consumes the fan-out results
- [../Queue/CLAUDE.md](./../Queue/CLAUDE.md) — `IMangaQueueService` provides BL-01 GUARD queue dedup for manga
- [../Jobs/](../Jobs/) — `TaskManager.defaultTasks` registers `MangaRssSyncCommand` + `MissingChapterSearchCommand` at runtime per Anti-pattern C compliance
- [../../Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) — `Manga/Release/MangaReleaseController.cs` (Plan 06-09) consumes `IMangaSearchForReleases.ChapterSearch` for the PIPELINE-01 Interactive Search modal
