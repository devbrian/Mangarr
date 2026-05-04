# NzbDrone.Core/IndexerSearch

## Purpose

The **search-and-dispatch surface** between the user/scheduled-task and the indexer fan-out. Owns:
- Search command POCOs (`SeriesSearchCommand`, `EpisodeSearchCommand`, `SeasonSearchCommand`, `MissingEpisodeSearchCommand`, `CutoffUnmetEpisodeSearchCommand`).
- Per-command `IExecute<T>` services that walk the command's input set, build per-message `SearchCriteria`, and dispatch to indexers via `ReleaseSearchService`.
- The fan-out + parallel-fetch + per-indexer try/catch-isolated `ReleaseSearchService` that calls `IIndexer.Fetch(SearchCriteria)` on every enabled indexer and aggregates the results into ranked `DownloadDecision` rows.
- The `Definitions/` subdirectory carries the `SearchCriteriaBase` hierarchy + `MangaSearchCriteriaBase` (Phase 3 D-06 parallel hierarchy).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\IndexerSearch\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `SeriesSearchCommand.cs` | TV bulk-SeriesId dispatch. Carries `int SeriesId / UserInvokedSearch`. |
| `SeriesSearchService.cs` | TV `IExecute<SeriesSearchCommand>`. Walks all monitored episodes for the series, dispatches `EpisodeSearchCommand` per affected season. |
| `SeasonSearchCommand.cs` / `SeasonSearchService.cs` | TV per-season dispatch. |
| `EpisodeSearchCommand.cs` | TV per-episode dispatch. Carries `List<int> EpisodeIds`. |
| `EpisodeSearchService.cs` | TV `IExecute<EpisodeSearchCommand>` + `IExecute<MissingEpisodeSearchCommand>` + `IExecute<CutoffUnmetEpisodeSearchCommand>`. Lines 110-161 are the canonical pattern referenced by Phase 6 Plan 06-06 (per-message dispatch + walking + queue dedup). |
| `MissingEpisodeSearchCommand.cs` | Wanted/Missing sweep — searches all monitored episodes without files. |
| `CutoffUnmetEpisodeSearchCommand.cs` | Cutoff sweep — searches episodes below their quality cutoff. |
| `ReleaseSearchService.cs` | Fan-out + parallel-fetch + per-indexer try/catch isolation. Implements `ISearchForReleases` interface. |
| `EpisodeSearchGroup.cs` | Helper for grouping search results by episode. |

## Subdirectories

### `Definitions/`
| File | Purpose |
|------|---------|
| `SearchCriteriaBase.cs` | Abstract base for TV search criteria. |
| `EpisodeSearchCriteria.cs` / `SeasonSearchCriteria.cs` / `SingleEpisodeSearchCriteria.cs` / `AnimeEpisodeSearchCriteria.cs` / `AnimeSeasonSearchCriteria.cs` / `DailyEpisodeSearchCriteria.cs` | TV per-shape criteria. |
| `MangaSearchCriteriaBase.cs` / `MangaSearchCriteria.cs` / `ChapterSearchCriteria.cs` | Phase 3 D-06 parallel hierarchy for manga. `ChapterNumber` is `decimal` per Phase 2 D-12 widen. `PreferredLanguages` is advisory only (Phase 3 D-07; Phase 5 TranslationProfile authoritative). |

## Phase 6 Manga Sibling

Phase 6 Plan 06-06 ships `IndexerSearch/Manga/` as a parallel sibling. **Phase 8 cleanup** will collapse with `IndexerSearch/` when `Tv/` deletes (and the `Protocol == DownloadProtocol.Http` filters drop along with the TV-side fan-out).

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`IndexerSearch/Manga/`](./Manga/CLAUDE.md) | Plan 06-06 | Manga search commands + RSS sync + dispatchers. **Commands**: `MangaSearchCommand` (D-06 bulk-MangaIds dispatch — NOT per-chapter fan-out — fired by `MissingChapterSearchService` and Add-Manga search-on-add), `ChapterSearchCommand` (D-12 single-element list shape — used by Plan 06-08 auto-retry + Plan 06-09 Interactive Search modal Grab POST), `MissingChapterSearchCommand` (D-09 Wanted sweep — `int? MangaId` null = all monitored), `MangaRssSyncCommand` (D-07 payload-less scheduled poll trigger). **Services**: `MangaReleaseSearchService` (manga-shape `Task<List<MangaDownloadDecision>>` fan-out — filters `IIndexerFactory.AutomaticSearchEnabled()` to `Protocol == DownloadProtocol.Http`, parallel-fans `Fetch(MangaSearchCriteria) / Fetch(ChapterSearchCriteria)` with per-indexer try/catch, runs Phase 5 `IMakeMangaDownloadDecision.GetSearchDecision`); `MangaSearchService` + `ChapterSearchService` per-command dispatchers; `MissingChapterSearchService` walker (groups by MangaId per D-09 — NOT per-chapter fan-out — pushes ONE `MangaSearchCommand` per affected Manga); `MangaRssSyncService` (filters RSS-enabled indexers to `Protocol == DownloadProtocol.Http`, honors per-`IndexerDefinition.SyncInterval > 0` override on top of global `Config.MangaRssSyncInterval`). **D-04 GUARD**: NO `IsSynthetic` filter anywhere — synthetic rows (Phase 2 D-17 metadata-only-count fallback) are treated identically to real-feed chapters. **BL-01 GUARD**: queue dedup uses `IMangaQueueService` (Plan 06-05), NOT TV's `IQueueService`. **Anti-pattern C compliance** (sonarr-consistency-audit SKILL.md): `MangaRssSyncCommand` + `MissingChapterSearchCommand` + Plan 06-08's `ProcessMangaCompletedCommand` are registered at runtime in `TaskManager.defaultTasks` — NOT seeded via 001 `Insert.IntoTable`. Phase 8 cleanup: collapse with `EpisodeSearchService` + `ReleaseSearchService` + `FetchAndParseRssService`. |
| `IndexerSearch/Definitions/MangaSearchCriteriaBase.cs` Manga/Chapter fully-qualified | Plan 06-06 | Namespace-collision fix: the new `NzbDrone.Core.IndexerSearch.Manga` namespace shadowed the bare `Manga.` prefix; the two `Manga.Manga / Manga.Chapter` references are now fully-qualified via the global root (`NzbDrone.Core.Manga.Manga / NzbDrone.Core.Manga.Chapter`). |

## Phase 6 D-04 + BL-01 Discipline

Two invariants to enforce when modifying any manga-side search code:

1. **D-04 IsSynthetic-treated-identically**: search/Wanted code paths must NOT add a `.Where(c => !c.IsSynthetic)` filter. Synthetic rows surface alongside real-feed rows. Plan 06-06 ships an explicit test asserting ONE `MangaSearchCommand` for a manga with mixed synthetic+real monitored chapters.
2. **BL-01 GUARD**: queue dedup uses `IMangaQueueService` (Plan 06-05 sibling), NEVER TV's `IQueueService`. The two services serve different rows.

## Manga Adaptation Notes

The fan-out + parallel-fetch + per-indexer try/catch pattern is preserved verbatim from `EpisodeSearchService.cs:110-161` and `FetchAndParseRssService.cs:14-62`. The two divergences are:
1. **Manga-shape return type** (`Task<List<MangaDownloadDecision>>`) — bridging through TV's `IProcessDownloadDecisions` would require an adapter that does not exist.
2. **`Protocol == DownloadProtocol.Http` filter** so TV and manga fan-outs co-exist on the same `IIndexerFactory.RssEnabled() / .AutomaticSearchEnabled()` calls without double-dispatching.

Phase 8 collapses both filters when `Tv/` deletes.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — `IIndexer.Fetch(SearchCriteria)` is the dispatch target
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `IMakeMangaDownloadDecision.GetSearchDecision` (Phase 5) consumes the fan-out results
- [../Queue/CLAUDE.md](./../Queue/CLAUDE.md) — `IMangaQueueService` provides BL-01 GUARD queue dedup for manga
- [../Jobs/](../Jobs/) — `TaskManager.defaultTasks` registers `MangaRssSyncCommand` + `MissingChapterSearchCommand` at runtime per Anti-pattern C compliance
- [../../Sonarr.Api.V5/CLAUDE.md](../../Sonarr.Api.V5/CLAUDE.md) — `Manga/Release/MangaReleaseController.cs` (Plan 06-09) consumes `IMangaSearchForReleases.ChapterSearch` for the PIPELINE-01 Interactive Search modal
