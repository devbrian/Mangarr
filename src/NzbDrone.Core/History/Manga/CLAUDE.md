# History/Manga

## Purpose

Phase 6 D-21 manga sibling of `src/NzbDrone.Core/History/` (TV `EpisodeHistory` family). Ships the `ChapterHistory` parallel-table substrate that closes BL-01 (cross-domain ID-collision class of bug) and powers the auto-retry orchestrator (Plan 06-08), grab→import correlation in the import pipeline (Plan 06-07), and the V5 history controller listing (Plan 06-09).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\History\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `ChapterHistory.cs` | `ModelBase` entity. `ChapterId` column is INDEPENDENT of `EpisodeHistory.EpisodeId` — separate Mapper.Entity registration in `TableMapping.cs`. Fields: MangaId, ChapterId, EventType, Date, SourceTitle, DownloadId, TranslatedLanguage, ScanlationGroup, SourceKey, ReleaseGuid, Data : Dictionary<string,string>, Successful. |
| `ChapterHistoryEventType.cs` | Enum with EXACTLY 5 values per HISTORY-01: Grabbed=1, DownloadFailed=2, Imported=3, ImportFailed=4, Ignored=5. Drops TV-specific EpisodeFileDeleted/Renamed/SeriesFolderImported. |
| `IChapterHistoryRepository.cs` + `ChapterHistoryRepository.cs` | `BasicRepository<ChapterHistory>` Dapper wrapper. Custom queries: FindByChapterId, FindByDownloadId, MostRecentForChapter/DownloadId, GetByManga, DeleteForManga, Since, GetPaged(spec, languages). |
| `IChapterHistoryService.cs` + `ChapterHistoryService.cs` | Event-driven service with 5 IHandle subscriptions. **Anti-Pattern guard**: history rows are NEVER written from inside the repository — always service-layer. |

## Patterns / Conventions

### Event-driven service (NOT repository)

Mirrors `HistoryService.HandleAsync(SeriesDeletedEvent)` precedent. Each Handle method builds a `ChapterHistory` row via object-initializer, populates `history.Data` per the per-EventType key set lock (Q-4 schema), and calls `_repository.Insert(history)`.

```csharp
public class ChapterHistoryService : IChapterHistoryService,
                                     IHandle<ChapterGrabbedEvent>,
                                     IHandle<ChapterImportedEvent>,
                                     IHandle<ChapterDownloadFailedEvent>,
                                     IHandle<DownloadIgnoredEvent>,
                                     IHandle<MangaDeletedEvent>
```

`Handle(DownloadIgnoredEvent)` is a TV-bail handler: the existing TV-shaped event carries `SeriesId` + `EpisodeIds` and is only emitted by TV providers in v1; the manga handler returns early to avoid TV ignores polluting `ChapterHistory`.

### Per-EventType Data dictionary key sets (RESEARCH §Q-4 lock)

| EventType | Required Data Keys |
|-----------|---------------------|
| Grabbed | Indexer, Size, Age, PublishedDate, DownloadClient, CustomFormatScore, Protocol |
| DownloadFailed | DownloadClient, Message, Source, Indexer |
| Imported | ChapterFileId, DroppedPath, ImportedPath, Size, DownloadClient |
| ImportFailed | DroppedPath, FailureReason, RejectionType *(Plan 06-07 will populate)* |
| Ignored | DownloadClient, Message, Indexer *(no manga emitter in v1; reserved)* |

Keys are JSON-camelCased on round-trip via `EmbeddedDocumentConverter<Dictionary<string,string>>` registered globally in `TableMapping.RegisterMappers`. Same behavior as TV `EpisodeHistory.Data`.

### BL-01 fix — separate-table guarantee

Because `Mapper.Entity<ChapterHistory>("ChapterHistory")` is a distinct registration from `Mapper.Entity<EpisodeHistory>("History")`, Dapper's `Query<ChapterHistory>` cannot hydrate from the TV `History` table even when both tables hold rows whose foreign-key int columns share a value. The `Queries_ChapterHistory_only_not_EpisodeHistory` test in `AlreadyImportedChapterSpecificationFixture` is the regression guard: seed `EpisodeHistory{EpisodeId=42}` against the real SQLite DB, then call `IsSatisfiedBy` for chapter 42 — assert Accept.

### Pitfall 4 GUARD on `Handle(ChapterImportedEvent)`

Plan 06-07 `ImportApprovedChapters` MUST publish `ChapterImportedEvent` AFTER the `ChapterFile` DB commit AND filesystem move have completed. The handler dereferences `message.ChapterFile` properties; a pre-commit publish would bind to a `ChapterFile` row whose Id is still 0.

## Manga Adaptation Notes

| Mangarr (TV) | Mangarr (manga) |
|-------------|-----------------|
| `EpisodeHistory.EpisodeId` | `ChapterHistory.ChapterId` (INDEPENDENT — separate column on a separate table) |
| `EpisodeHistory.SeriesId` | `ChapterHistory.MangaId` |
| `EpisodeHistory.Quality : QualityModel` | *(dropped — manga has no quality model per Phase 5 D-04)* |
| `EpisodeHistory.Languages : List<Language>` | `ChapterHistory.TranslatedLanguage : string` (BCP-47 per Phase 3 D-Q4) |
| *(none)* | `ChapterHistory.ScanlationGroup`, `SourceKey`, `ReleaseGuid` (D-11 release identity triple) |
| `EpisodeHistoryEventType` 8 values | `ChapterHistoryEventType` 5 values (drops EpisodeFileDeleted/Renamed/SeriesFolderImported) |
| `IHandle<EpisodeGrabbedEvent>` | `IHandle<ChapterGrabbedEvent>` |
| `IHandle<EpisodeImportedEvent>` | `IHandle<ChapterImportedEvent>` |
| `IHandle<DownloadFailedEvent>` (TV-shaped) | `IHandle<ChapterDownloadFailedEvent>` (manga-shaped) |
| `IHandle<EpisodeFileDeletedEvent>` | *(dropped — manga has no per-file delete history rows in v1)* |
| `IHandle<EpisodeFileRenamedEvent>` | *(dropped — manga has no rename history in v1)* |
| `IHandle<SeriesDeletedEvent>` | `IHandle<MangaDeletedEvent>` |
| `IHandle<DownloadIgnoredEvent>` | `IHandle<DownloadIgnoredEvent>` (TV-bail handler) |

### Phase 8 collapse plan

When `Tv/` deletes (Phase 8 milestone), this directory collapses with `src/NzbDrone.Core/History/`. The manga-shaped `ChapterId`/`MangaId`/`TranslatedLanguage`/`ScanlationGroup`/`SourceKey`/`ReleaseGuid` fields become the canonical history shape (no quality, single string language, release-identity triple). Until then both families coexist as parallel siblings.

## Cross-References

- TV side: [src/NzbDrone.Core/History/EpisodeHistory.cs](../EpisodeHistory.cs), [HistoryService.cs](../HistoryService.cs), [HistoryRepository.cs](../HistoryRepository.cs)
- Schema: [001_mangarr_baseline.cs lines 705-729](../../Datastore/Migration/001_mangarr_baseline.cs)
- TableMapping: [src/NzbDrone.Core/Datastore/TableMapping.cs](../../Datastore/TableMapping.cs) — registration after `ChapterFile`
- Spec consumer: [src/NzbDrone.Core/DecisionEngine/Manga/Specifications/AlreadyImportedChapterSpecification.cs](../../DecisionEngine/Manga/Specifications/AlreadyImportedChapterSpecification.cs) — Phase 6 D-21 STUB body replacement
- Events consumed: [ChapterGrabbedEvent](../../MediaFiles/ChapterArchiving/ChapterGrabbedEvent.cs), [ChapterImportedEvent](../../MediaFiles/MangaImport/ChapterImportedEvent.cs), [ChapterDownloadFailedEvent](../../MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs), [MangaDeletedEvent](../../Manga/Events/MangaDeletedEvent.cs)
- Tests: [src/NzbDrone.Core.Test/HistoryTests/Manga/](../../../NzbDrone.Core.Test/HistoryTests/Manga/) — `ChapterHistoryRepositoryFixture` (4 tests incl. BL-01 regression) + `ChapterHistoryServiceFixture` (6 tests covering each handler)
- Plan: [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-03-PLAN.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-03-PLAN.md)
