# IndexerSearch/Manga

## Purpose

Phase 6 D-06/D-07/D-08/D-09 manga sibling of `src/NzbDrone.Core/IndexerSearch/` (TV `EpisodeSearch` / `SeriesSearch` / `MissingEpisodeSearch` / RSS-sync family). Ships the four manga search command POCOs + four `IExecute<TCommand>` services that drive Interactive Search (PIPELINE-01), Add-Manga search (D-06), scheduled RSS poll (PIPELINE-02), and Wanted/Missing sweep (WANTED-02). Decisions are produced by Phase 5 `MangaDownloadDecisionMaker`; the grab + import path is owned by Plan 06-07/08.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\IndexerSearch\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaSearchCommand.cs` | Command POCO carrying `List<int> MangaIds` (D-06 bulk dispatch — NOT per-chapter fan-out) + `UserInvokedSearch` flag. `SendUpdatesToClient => true` so the React Activity panel sees progress. Role-match analog: `SeriesSearchCommand.cs`. |
| `ChapterSearchCommand.cs` | Command POCO carrying `List<int> ChapterIds` for single-chapter / Interactive Search and the D-12 auto-retry consumer (single-element list per failed chapter). Role-match analog: `EpisodeSearchCommand.cs`. |
| `MissingChapterSearchCommand.cs` | Command POCO. `MangaId : int?` (null = "all monitored mangas") + `Monitored = true` default. Role-match analog: `MissingEpisodeSearchCommand.cs`. |
| `MangaRssSyncCommand.cs` | Command POCO (no payload). Triggered by the `TaskManager.defaultTasks` scheduler at the global `Config.MangaRssSyncInterval` cadence. |
| `IMangaSearchForReleases.cs` | Interface: `MangaSearch(MangaSearchCriteria)` + `ChapterSearch(ChapterSearchCriteria)` returning `Task<List<MangaDownloadDecision>>` (manga-shape). Role-match analog: `ISearchForReleases`. |
| `MangaReleaseSearchService.cs` | `IMangaSearchForReleases` impl. Filters `IIndexerFactory.AutomaticSearchEnabled()` to `Protocol == DownloadProtocol.Http`, parallel-fans `Fetch(MangaSearchCriteria)` / `Fetch(ChapterSearchCriteria)` with per-indexer try/catch isolation, runs Phase 5 `IMakeMangaDownloadDecision.GetSearchDecision`. Role-match analog: `ReleaseSearchService.cs`. |
| `MangaSearchService.cs` | `IExecute<MangaSearchCommand>`. Walks `MangaIds`, builds `MangaSearchCriteria` carrying ONLY monitored chapters with no `ChapterFile` (D-04 honors `IsSynthetic` identically — no filter), delegates to `IMangaSearchForReleases`. Role-match analog: `EpisodeSearchService.Execute(EpisodeSearchCommand)`. |
| `ChapterSearchService.cs` | `IExecute<ChapterSearchCommand>`. Walks `ChapterIds`, builds `ChapterSearchCriteria`, propagates `CommandTrigger.Manual` into `UserInvokedSearch`. |
| `MissingChapterSearchService.cs` | `IExecute<MissingChapterSearchCommand>`. Walks `IChapterService.AllMissingMonitoredChapters()` (or `GetChaptersByManga` for the single-MangaId path), dedups against `IMangaQueueService.GetMangaQueue()`, **groups by MangaId per D-09**, pushes ONE `MangaSearchCommand` per affected Manga via `IManageCommandQueue.Push`. |
| `MangaRssSyncService.cs` | `IExecute<MangaRssSyncCommand>`. Filters `IIndexerFactory.RssEnabled()` to `Protocol == DownloadProtocol.Http`, honors per-`IndexerDefinition.SyncInterval > 0` override on top of global `Config.MangaRssSyncInterval` (D-07), per-indexer try/catch isolation, runs Phase 5 `GetRssDecision`. **Phase 40 RSS self-heal**: after `GetRssDecision` and BEFORE the grab pipeline, groups decisions by resolved `RemoteChapter.Manga` and calls `IChapterSynthesisService.SynthesizeFromDecisions` per manga (best-effort, per-manga try/catch) so uncataloged gateway chapters surfaced only in `/recent` become wanted — the on-search/on-grab synthesis hooks did not cover the scheduled RSS path. The per-release ID-match-else-EXACT-title attribution gate inside synthesis is the precision guard (fuzzy-only resolutions are not backfilled). Role-match analog: `FetchAndParseRssService.cs`. |

## Patterns / Conventions

### D-06: bulk MangaSearchCommand (NOT per-chapter fan-out)

```csharp
// MangaSearchService.Execute
foreach (var mangaId in message.MangaIds)
{
    var manga = _mangaService.GetManga(mangaId);
    var chapters = _chapterService.GetChaptersByManga(mangaId)
        .Where(c => c.Monitored && c.ChapterFileId == null)
        .ToList();
    var criteria = new MangaSearchCriteria { Manga = manga, Chapters = chapters, ... };
    var decisions = _releaseSearchService.MangaSearch(criteria).GetAwaiter().GetResult();
}
```

ONE indexer round-trip per Manga (each indexer's `Fetch(MangaSearchCriteria)` returns the full release list for that manga). Per-chapter fan-out — 199 commands for a 199-chapter manga — was rejected. Per-source rate budget naturally respected because commands serialize through `IManageCommandQueue`.

### D-07: per-IndexerDefinition.SyncInterval override

```csharp
private bool DueForRefresh(IIndexer indexer, TimeSpan globalInterval)
{
    if (indexer.Definition is not IndexerDefinition def) return true;
    var perIndexerOverride = def.SyncInterval > 0
        ? TimeSpan.FromMinutes(def.SyncInterval)
        : (TimeSpan?)null;
    var effective = perIndexerOverride ?? globalInterval;
    if (def.LastRssSync == null) return true;
    return (DateTime.UtcNow - def.LastRssSync.Value) >= effective;
}
```

`SyncInterval == 0` means "use global"; never-synced indexers (`LastRssSync == null`) are treated as due. Power users can flip MangaDex to 5min while keeping comix.to at 30min. Plan 07 React Settings UI surfaces the override field.

### D-09: group-by-MangaId in MissingChapterSearchService

```csharp
var grouped = chapters
    .Where(c => !queuedChapterIds.Contains(c.Id))   // BL-01 GUARD: queue dedup
    .GroupBy(c => c.MangaId)
    .ToList();
foreach (var group in grouped)
{
    _commandQueueManager.Push(
        new MangaSearchCommand(new List<int> { group.Key }, userInvoked: ...));
}
```

Mirrors Sonarr's `MissingEpisodeSearchCommand` shape exactly. **BL-01 GUARD**: queue dedup uses `IMangaQueueService.GetMangaQueue()` (Plan 06-05 — D-20), NOT TV's `IQueueService.GetQueue()` whose `Episodes.Id` collides with `Chapter.Id`. **D-04 GUARD**: `IsSynthetic=true` rows are NOT filtered — they're treated identically to real-feed chapters per CONTEXT.md D-04.

### Anti-pattern C compliance: TaskManager.defaultTasks (NOT Insert.IntoTable)

```csharp
// TaskManager.cs — defaultTasks block (Phase 6 entries):
new ScheduledTask
{
    Interval = GetMangaRssSyncInterval(),
    TypeName = typeof(MangaRssSyncCommand).FullName
},
new ScheduledTask
{
    Interval = 24 * 60,
    TypeName = typeof(MissingChapterSearchCommand).FullName
}
```

The Phase 2 retro found a class of bug where a migration `Insert.IntoTable("ScheduledTasks")` seeded a row that should have been registered via runtime `TaskManager.defaultTasks`. The accompanying fixture was a textual grep of the migration source, so the test was green AND the prod code was wrong. Plan 06-06 reapplies the remediation: registration lives in `TaskManager` (verified by the structural `TaskManagerDefaultTasksFixture`); `001_mangarr_baseline.cs` Insert-IntoTable count is unchanged at 0 (verified by the same fixture's third test).

### Per-indexer try/catch isolation

```csharp
// Both MangaReleaseSearchService.FetchFromIndexers and MangaRssSyncService.FetchIndexerSafe:
try { return await fetch(indexer); }
catch (Exception ex)
{
    _logger.Error(ex, "Error during manga {0} on {1}", searchKind, indexer.Definition.Name);
    return Array.Empty<ReleaseInfo>();
}
```

Mirrors `FetchAndParseRssService.FetchIndexer` lines 49-61. One bad indexer (timeout, 500, parser exception) does NOT kill the batch — the surviving indexers' reports still flow through to the decision maker.

## Manga Adaptation Notes

### TV-vs-manga mapping

| TV (Sonarr) | Manga (Mangarr) | Difference |
|-------------|-----------------|------------|
| `SeriesSearchCommand { int SeriesId }` | `MangaSearchCommand { List<int> MangaIds, bool UserInvokedSearch }` | D-06 bulk dispatch |
| `EpisodeSearchCommand { List<int> EpisodeIds }` | `ChapterSearchCommand { List<int> ChapterIds }` | Field rename |
| `MissingEpisodeSearchCommand { int? SeriesId, bool Monitored }` | `MissingChapterSearchCommand { int? MangaId, bool Monitored }` | Field rename |
| `RssSyncCommand` (TV-side, polls all RssEnabled indexers) | `MangaRssSyncCommand` (filters to `Protocol == Http`) | Manga-protocol filter |
| `ISearchForReleases` returns `Task<List<DownloadDecision>>` | `IMangaSearchForReleases` returns `Task<List<MangaDownloadDecision>>` | Phase 5 D-05 — manga decision DTO carries `RemoteChapter` |
| `EpisodeSearchService` runs `IProcessDownloadDecisions` on results | `MangaSearchService` returns decisions directly | TV `IProcessDownloadDecisions` cannot consume `MangaDownloadDecision`; the grab path lives in Plan 06-07 ImportApprovedChapters / Plan 06-08 AutoRetryOrchestrator |
| `IIndexerFactory.AutomaticSearchEnabled()` (no protocol filter) | Same call + `.Where(i => i.Protocol == DownloadProtocol.Http)` | Manga-only fan-out |

### Indexer registration choice: AutomaticSearchEnabled (NOT InteractiveSearchEnabled)

Plan 06-06 wires the scheduled / wanted-sweep / on-add code paths — those land in `AutomaticSearchEnabled` per Sonarr's existing `EnableAutomaticSearch` per-indexer toggle. The Interactive Search modal flavor (PIPELINE-01 user-facing UX) lands in Plan 06-09 V5 controller wiring + Plan 07 React modal; that path will use `InteractiveSearchEnabled`.

### Phase 8 collapse plan

When `Tv/` deletes:

1. Drop `Protocol == DownloadProtocol.Http` filters from `MangaReleaseSearchService` and `MangaRssSyncService` — only manga indexers will remain.
2. Collapse `MangaSearchCommand` ↔ `SeriesSearchCommand`, `ChapterSearchCommand` ↔ `EpisodeSearchCommand`, `MissingChapterSearchCommand` ↔ `MissingEpisodeSearchCommand`, `MangaRssSyncCommand` ↔ `RssSyncCommand`. Pick the manga names; drop the TV ones.
3. Collapse `IMangaSearchForReleases` ↔ `ISearchForReleases` — pick the manga signature (returns `MangaDownloadDecision`); the TV decision flow is gone.
4. Remove `// Sonarr divergence: ...` headers from all files in this directory — they're no longer divergent.
5. Move the four services + interface up one level to `IndexerSearch/`; delete the `Manga/` subfolder.

## Cross-References

- [IndexerSearch/](../CLAUDE.md) — TV Search family
- [DecisionEngine/Manga/](../../DecisionEngine/Manga/CLAUDE.md) — `MangaDownloadDecisionMaker` consumed by these services
- [Queue/Manga/](../../Queue/Manga/CLAUDE.md) — `IMangaQueueService.GetMangaQueue` consumed by `MissingChapterSearchService` queue dedup
- [Indexers/](../../Indexers/CLAUDE.md) — `IIndexerFactory.RssEnabled` / `.AutomaticSearchEnabled` + `IIndexer.Fetch(MangaSearchCriteria)` / `.FetchRecent`
- [Jobs/TaskManager.cs](../../Jobs/TaskManager.cs) — `defaultTasks` registration of the two scheduled commands
- [.claude/skills/sonarr-consistency-audit/SKILL.md](../../../../.claude/skills/sonarr-consistency-audit/SKILL.md) — Anti-pattern C compliance (NOT seeded via 001 Insert.IntoTable)
- [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-CONTEXT.md) — D-04/D-06/D-07/D-08/D-09 decisions
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — Phase 6 plan 06-06 entries
