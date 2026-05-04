# NzbDrone.Core/History

## Purpose

Persistent grab/import history — every download attempt is recorded with outcome (`Grabbed / DownloadFailed / Imported / ImportFailed / Ignored / EpisodeFileDeleted / EpisodeFileRenamed / SeriesFolderImported`). Backed by the `EpisodeHistory` table; read-side surfaces (HISTORY-02 listing, HISTORY-03 retry, AlreadyImported decision-spec) consume via `IHistoryService`.

History is an **event-driven write surface** (RESEARCH §Anti-Pattern: NEVER write history from inside the Repository). The service subscribes to download-pipeline events and writes one row per outcome.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\History\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `EpisodeHistory.cs` | TV history row entity. Carries `EpisodeId / SeriesId / SourceTitle / Quality / Languages / Date / EventType / Data dictionary`. The `Data` dictionary is a per-EventType wire-shape: each EventType populates a known set of keys (RESEARCH §Q-4 schema lock). |
| `HistoryRepository.cs` | Dapper `BasicRepository<EpisodeHistory>`. Custom queries: `FindByDownloadId`, `MostRecentForEpisode`, `GetByEpisode`, `GetByMovie`, paged `GetPaged(spec, languages)`. |
| `HistoryService.cs` | Event-driven service. Subscribes to: `EpisodeGrabbedEvent`, `EpisodeImportedEvent`, `DownloadFailedEvent`, `DownloadCompletedEvent`, `EpisodeFileDeletedEvent`, `EpisodeFileRenamedEvent`, `DownloadIgnoredEvent`, `SeriesDeletedEvent` (cascade delete via `_repository.DeleteForSeries`). |

## Phase 6 Manga Sibling

Phase 6 Plan 06-03 ships `History/Manga/` as a parallel sibling. **The separate sibling table is the BL-01 mechanical guarantee** — `ChapterHistory.ChapterId` is INDEPENDENT of `EpisodeHistory.EpisodeId`, so Dapper `Query<ChapterHistory>` cannot leak TV rows even when manga and series int IDs collide.

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`History/Manga/`](./Manga/CLAUDE.md) | Plan 06-03 | Manga-side history. **`ChapterHistory` + `ChapterHistoryEventType`**: parallel sibling to `EpisodeHistory` + `EpisodeHistoryEventType`. **BL-01 fix**: separate `Mapper.Entity` registration in `TableMapping.cs` (line 117-118 in DIVERGENCE.md) so Dapper queries cannot cross-contaminate between tables. **EventType** has EXACTLY 5 values per HISTORY-01: `Grabbed=1, DownloadFailed=2, Imported=3, ImportFailed=4, Ignored=5` (collapses TV's `EpisodeFileDeleted / EpisodeFileRenamed / SeriesFolderImported / DownloadFolderImported` into `Imported`). **Drops** `Quality` (no quality model per Phase 5 D-04); **replaces** `Languages : List<Language>` with `TranslatedLanguage : string` (BCP-47); **adds** `ScanlationGroup + SourceKey + ReleaseGuid` (D-11 release-identity triple). **`IChapterHistoryRepository` + `ChapterHistoryRepository`**: BasicRepository wrapper with `FindByChapterId` (BL-01 fix consumer site), `FindByDownloadId`, `MostRecentForChapter / MostRecentForDownloadId`, `GetByManga`, `DeleteForManga`, `Since`, `GetPaged(spec, languages)`. **`IChapterHistoryService` + `ChapterHistoryService`**: 5 IHandle subscriptions — `ChapterGrabbedEvent`, `ChapterImportedEvent`, `ChapterDownloadFailedEvent`, `DownloadIgnoredEvent` (TV-bail handler — manga ignores route via ChapterDownloadFailedEvent in v1), `MangaDeletedEvent` (cascade). Per-EventType `Data` dictionary key set per RESEARCH §Q-4. Phase 8 cleanup: collapse with `EpisodeHistory` / `HistoryService` / `HistoryRepository`. |
| `DecisionEngine/Manga/Specifications/AlreadyImportedChapterSpecification.cs` STUB body replacement | Plan 06-03 | Replaces the Phase 5 Accept-always STUB body with `_chapterHistoryService.FindByChapterId(chapter.Id).FirstOrDefault(h => h.EventType == ChapterHistoryEventType.Imported)`. **BL-01 fix verbatim**: queries the new `ChapterHistory.ChapterId` column — independent of `EpisodeHistory.EpisodeId`. |

## BL-01 Cross-Domain ID-Collision Class of Bug

This is the prevention pattern: when manga `Manga.Id` and TV `Series.Id` int values collide (which they will, as the migrations seed both), a query like `_historyRepo.FindByEpisodeId(123)` against the TV `EpisodeHistory` table would silently return TV rows when the caller meant to find manga rows. The mitigation is a separate sibling table (`ChapterHistory`) with a separate `Mapper.Entity` registration so Dapper has no path to cross-contaminate.

**Plan 06-03 ships the BL-01 regression-guard test (`ChapterHistoryRepositoryFixture`) at the SQLite persistence layer:** insert collision rows into both tables, query each, assert zero leakage.

## Manga Adaptation Notes

History is the canonical event-driven write surface. The TV pattern (events → service handler → repository write) transfers verbatim; the only divergence is the BL-01-mitigation sibling table + the manga-specific EventType collapse + the drop-quality / add-language-and-group field shape.

Phase 8 collapses the sibling tables into a unified `History` namespace when domain rename runs.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — Events that history consumes
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `AlreadyImportedSpecification` (TV) + `AlreadyImportedChapterSpecification` (manga) consume the history
- [../Datastore/CLAUDE.md](../Datastore/CLAUDE.md) — `TableMapping.cs` registers the sibling tables
- [../../Sonarr.Api.V5/CLAUDE.md](../../Sonarr.Api.V5/CLAUDE.md) — `Manga/History/ChapterHistoryController.cs` exposes the manga history over REST (HISTORY-01..03)
