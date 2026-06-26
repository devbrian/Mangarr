# Blocklisting/Manga

## Purpose

Phase 6 D-11 + D-19 manga sibling of `src/NzbDrone.Core/Blocklisting/` (TV `Blocklist` family). Ships the `MangaBlocklist` parallel-table substrate that wires `BlocklistSpecification` (Phase 5 D-19 STUB-replacement target) and powers the auto-blocklist + auto-retry orchestration in Plan 06-08.


## Key Files

| File | Purpose |
|------|---------|
| `MangaBlocklist.cs` | `ModelBase` entity. D-11 release-identity triple `(SourceKey, ReleaseGuid, SourceTitle)`. Fields: MangaId, ChapterIds : List<int>, SourceTitle, SourceKey, ReleaseGuid, ReleaseInfoJson, Date, Reason, Source. `ChapterIds` round-trips via the global `EmbeddedDocumentConverter<List<int>>` registered in `TableMapping.RegisterMappers`. |
| `MangaBlocklistAddedEvent.cs` | Event published by `MangaBlocklistService` AFTER `_repository.Insert(...)` returns. **Event-ordering contract for Plan 06-08 AutoRetryOrchestrator**: subscribers see the just-committed row when they query the repository in response. Synchronous fan-out via Mangarr's `IEventAggregator` guarantees ordering. |
| `IMangaBlocklistRepository.cs` + `MangaBlocklistRepository.cs` | `BasicRepository<MangaBlocklist>` Dapper wrapper. Custom queries: `BlocklistedByTitle(int mangaId, string sourceTitle)`, `BlocklistedByReleaseGuid(int mangaId, string releaseGuid)`, `BlocklistedByManga(int mangaId)`, `DeleteForManga(int mangaId)`. Repository narrows by MangaId; service layer applies Pitfall 5 matching. |
| `IMangaBlocklistService.cs` + `MangaBlocklistService.cs` | Event-driven service. **Anti-Pattern guard**: blocklist rows are NEVER written from inside the repository — always service-layer. Implements `IExecute<ClearMangaBlocklistCommand>` + `IHandle<ChapterDownloadFailedEvent>` (auto-blocklist on terminal failure) + `IHandleAsync<MangaDeletedEvent>` (cascade cleanup). |
| `ClearMangaBlocklistCommand.cs` | UI button "Clear blocklist" command (manga sibling of `ClearBlocklistCommand`). |

## Patterns / Conventions

### Event-driven service (NOT repository)

Mirrors `BlocklistService.Handle(DownloadFailedEvent)` precedent. The handler builds a `MangaBlocklist` row, populates the D-11 release-identity triple from the event's `ChapterDownloadFailedEvent.Release` field (Plan 06-01 extension), and writes via the repository.

```csharp
public class MangaBlocklistService : IMangaBlocklistService,
                                     IExecute<ClearMangaBlocklistCommand>,
                                     IHandle<ChapterDownloadFailedEvent>,
                                     IHandleAsync<MangaDeletedEvent>
```

### Pitfall 5 mitigation in `Blocklisted(int, ReleaseInfo)`

The matching algorithm is deliberately defensive against the auto-retry-loop hazard:

| Field | Mitigation |
|-------|------------|
| `Title` | Trim + `ToLowerInvariant` both sides → compare with `OrdinalIgnoreCase` |
| `ReleaseGuid` | Null-tolerant: when either side is null/empty, match-on-`(Title, SourceKey)` only. Otherwise `OrdinalIgnoreCase` compare. **(debug `auto-retry-loop-guid-mismatch`, 2026-06-13 — the failure-path `Release` rebuilt by `MangaTrackedDownloadService.MapFromHistory` drops the gateway Guid, so the blocklisted row stores `ReleaseGuid = null` while the re-search release carries the gateway's required non-empty guid; a strict compare never re-matched → `AutoRetryOrchestrator` re-grabbed the same release every ~5s forever. Made symmetric with the SourceKey fallback below.)** |
| `SourceKey` | Null-tolerant: when either side is null/empty, match-on-`(Title, Guid)` only. Otherwise `OrdinalIgnoreCase` compare. |

`ReleaseInfo.Indexer` carries the manga `SourceKey` value at the wire layer (per Phase 3 D-17 + Plan 06-03 `ChapterHistoryService.Handle` precedent at line 141). The service reads `release.Indexer` as the SourceKey input; the column on `MangaBlocklist` is named `SourceKey` for canonical clarity.

### Ordering invariant for Plan 06-08 AutoRetryOrchestrator

```csharp
// 1. Insert FIRST — row must be committed before any event handler runs.
_repository.Insert(blocklist);

// 2. THEN publish the event. Mangarr's IEventAggregator.PublishEvent is synchronous
//    fan-out, so any subscriber (e.g., AutoRetryOrchestrator) sees the inserted row
//    when it queries the repository in response.
_eventAggregator.PublishEvent(new MangaBlocklistAddedEvent(blocklist, message));
```

Both the auto-blocklist path (`Handle(ChapterDownloadFailedEvent)`) and the manual UI insert path (`Block(MangaBlocklist)`) follow the same ordering. Plan 06-08 subscribes to `MangaBlocklistAddedEvent` (NOT `ChapterDownloadFailedEvent`) so the re-search cannot fire before the blocklist row exists; `BlocklistSpecification` then correctly rejects the just-blocklisted release on the next decision pass.

### BL-01-style separate-table guarantee

Because `Mapper.Entity<MangaBlocklist>("MangaBlocklist")` is a distinct registration from `Mapper.Entity<Blocklist>("Blocklist")`, Dapper's `Query<MangaBlocklist>` cannot hydrate from the TV `Blocklist` table even when both tables hold rows whose foreign-key int columns share a value. Mirrors the BL-01 mechanical guarantee documented in `History/Manga/CLAUDE.md`.

## Manga Adaptation Notes

| Mangarr (TV) | Mangarr (manga) |
|-------------|-----------------|
| `Blocklist.SeriesId` | `MangaBlocklist.MangaId` |
| `Blocklist.EpisodeIds : List<int>` | `MangaBlocklist.ChapterIds : List<int>` |
| `Blocklist.Quality : QualityModel` | *(dropped — manga has no quality model per Phase 5 D-04)* |
| `Blocklist.Languages : List<Language>` | *(dropped — TranslatedLanguage already lives on the underlying ChapterHistory; not duplicated on the blocklist row)* |
| `Blocklist.Indexer : string` | `MangaBlocklist.SourceKey : string` (Phase 3 D-17 canonical key) |
| `Blocklist.Protocol : DownloadProtocol` | *(dropped — v1 manga uses only DownloadProtocol.Http)* |
| `Blocklist.TorrentInfoHash : string` | *(dropped — no torrent path in v1)* |
| `Blocklist.PublishedDate : DateTime?` | *(dropped — release date lives on ReleaseInfoJson if needed)* |
| *(none)* | `MangaBlocklist.SourceKey + ReleaseGuid` (D-11 release identity triple components) |
| `IBlocklistService.Blocklisted(int seriesId, ReleaseInfo)` | `IMangaBlocklistService.Blocklisted(int mangaId, ReleaseInfo)` |
| `IBlocklistService.BlocklistedTorrentHash` | *(dropped — no torrent path)* |
| `IHandle<DownloadFailedEvent>` (TV-shaped) | `IHandle<ChapterDownloadFailedEvent>` (manga-shaped) |
| `IHandleAsync<SeriesDeletedEvent>` | `IHandleAsync<MangaDeletedEvent>` |
| *(no analog)* | `MangaBlocklistAddedEvent` (event-ordering contract for Plan 06-08) |

### Phase 8 collapse plan

When `Tv/` deletes (Phase 8 milestone), this directory collapses with `src/NzbDrone.Core/Blocklisting/`. The manga-shaped `MangaId`/`ChapterIds`/`SourceKey`/`ReleaseGuid` fields become the canonical blocklist shape (no quality, no torrent hash, single canonical source key). `MangaBlocklistAddedEvent` may be retained as the canonical post-Insert event hook even after collapse — its consumers (auto-retry orchestrator) survive the rename.

## Cross-References

- TV side (the Sonarr peers these manga files were modelled on): `src/NzbDrone.Core/Blocklisting/{Blocklist,BlocklistService,BlocklistRepository,ClearBlocklistCommand}.cs` — DELETED in the Phase 15 `Tv/` removal; cited for provenance only, absent at HEAD (the `Blocklisting/Manga/` subtree is now the sole content of this directory).
- Schema: [001_mangarr_baseline.cs MangaBlocklist table](../../Datastore/Migration/001_mangarr_baseline.cs)
- TableMapping: [src/NzbDrone.Core/Datastore/TableMapping.cs](../../Datastore/TableMapping.cs) — registration after `ChapterHistory`
- Spec consumer: [src/NzbDrone.Core/DecisionEngine/Manga/Specifications/BlocklistSpecification.cs](../../DecisionEngine/Manga/Specifications/BlocklistSpecification.cs) — Phase 6 D-19 STUB body replacement
- Event consumed: [ChapterDownloadFailedEvent](../../MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs) (Plan 06-01 extension), [MangaDeletedEvent](../../Manga/Events/MangaDeletedEvent.cs)
- Future consumer: Plan 06-08 `AutoRetryOrchestrator` subscribes to `MangaBlocklistAddedEvent` — guarantees blocklist row is committed before re-search fires.
- Tests: [src/NzbDrone.Core.Test/Blocklisting/Manga/](../../../NzbDrone.Core.Test/Blocklisting/Manga/) — `MangaBlocklistRepositoryFixture` (4 tests) + `MangaBlocklistServiceFixture` (15 tests covering 7 Pitfall 5 variants — happy/case/trim/null-SourceKey + null-Guid-on-seed/empty-Guid-on-search/both-guids-differ-no-overmatch guard (debug `auto-retry-loop-guid-mismatch`) — + ordering invariant + null-Release tolerance + cascade delete + Clear command + Block ordering)
- Plan: [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-04-PLAN.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-04-PLAN.md)
