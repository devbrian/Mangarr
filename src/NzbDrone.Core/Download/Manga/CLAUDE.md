# Download/Manga

## Purpose

Phase 6 Plan 06-08 staging-handoff orchestration. Bridges Phase 4's archived-CBZ output (`ChapterDownloadState.Status=Completed` + `ChapterArchivedEvent`) to Phase 6's `ImportApprovedChapters` (Plan 06-07) via a hybrid event-handler + scheduled-poller pattern, and runs the bounded auto-retry orchestration loop that closes the *arr self-healing pipeline (D-12 / D-13 / Pitfall 5).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Download\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `ProcessMangaCompletedCommand.cs` | Payload-less `Command` POCO. `SendUpdatesToClient => false` (poll-path scheduled task — no UI surface). Registered in `TaskManager.defaultTasks` at 1-minute cadence per Anti-pattern C compliance. |
| `ProcessMangaCompletedDownloads.cs` | Hybrid orchestrator: `IHandle<ChapterArchivedEvent>` (reactive happy path) + `IExecute<ProcessMangaCompletedCommand>` (1-min resilience poll). Both paths converge on a single `ProcessOne(chapterId, stagingPath)` method that idempotency-checks against `IChapterFileService.GetFilesByChapter`, runs `IMakeMangaImportDecision.GetDecision`, and dispatches into `IImportApprovedChapters.Import`. On success: deletes the `ChapterDownloadState` row + scratch dir per Phase 4 D-08 lifecycle. |
| `AutoRetryOrchestrator.cs` | Bounded auto-retry loop: `IHandle<MangaBlocklistAddedEvent>` (NOT `ChapterDownloadFailedEvent`). Counts prior `ChapterHistory{DownloadFailed}` rows via `IChapterHistoryService.FindByChapterId`; if count < `IConfigService.MaxAutoRetriesPerChapter`, pushes a fresh `ChapterSearchCommand` for the failed chapter — `BlocklistSpecification` then rejects the just-failed release and the next-best ranks up. After N retries exhausted, the chapter sits in History as `DownloadFailed` for manual user retry (HISTORY-03). |

## Patterns / Conventions

### RESEARCH Pattern 1 — hybrid event-handler + scheduled-poller

`ProcessMangaCompletedDownloads` implements BOTH interfaces:

```csharp
public class ProcessMangaCompletedDownloads :
    IHandle<ChapterArchivedEvent>,           // REACTIVE — Phase 4 fires within ms of archive
    IExecute<ProcessMangaCompletedCommand>   // POLL — TaskManager.defaultTasks 1-min cadence
{
    public void Handle(ChapterArchivedEvent message)  => ProcessOne(message.ChapterId, message.StagingPath);
    public void Execute(ProcessMangaCompletedCommand cmd)
    {
        var rows = _stateRepo.ByStatus(ChapterDownloadStatus.Completed).ToList();
        foreach (var s in rows) ProcessOne(s.ChapterId, s.StagingPath);
    }
}
```

The reactive path is the happy case (sub-second latency from archive completion to import dispatch). The poll path is resilience for: (a) process restart between archive completion and import (event lost), (b) handler exception missed by event bus, (c) Phase 6 import-spec rejection where the row is held for retry. **Idempotency is the contract guard**: `ProcessOne` short-circuits when `IChapterFileService.GetFilesByChapter(chapterId).Any()` — both paths can fire for the same chapter without double-importing.

The `Both_paths_invoking_same_chapter_only_imports_once_idempotency_contract` fixture asserts this: simulate a reactive Handle that successfully imports + a follow-up poll Execute against the same chapter — `IImportApprovedChapters.Import` is invoked exactly once.

### Anti-Pattern C compliance — TaskManager.defaultTasks (NOT migration seed)

```csharp
// TaskManager.cs — defaultTasks block (Plan 06-08 entry):
new ScheduledTask
{
    Interval = 1,
    TypeName = typeof(ProcessMangaCompletedCommand).FullName
}
```

The Phase 2 retro found a class of bug where a migration `Insert.IntoTable("ScheduledTasks")` seeded a row that should have been registered via runtime `TaskManager.defaultTasks`. The accompanying fixture was a textual grep of the migration source, so the test was green AND the prod code was wrong. Plan 06-08 reapplies the remediation: registration lives in `TaskManager` (verified by the structural `TaskManagerDefaultTasksFixture.TaskManager_defaultTasks_registers_ProcessMangaCompletedCommand` test); `001_mangarr_baseline.cs` `Insert.IntoTable` count remains 0 (verified by the same fixture's third test).

### AutoRetryOrchestrator subscribes to `MangaBlocklistAddedEvent` — NOT `ChapterDownloadFailedEvent` (Plan 06-04 ↔ 06-08 anti-race contract)

```csharp
public class AutoRetryOrchestrator : IHandle<MangaBlocklistAddedEvent>  // ← post-Insert event
{
    public void Handle(MangaBlocklistAddedEvent message) { /* push ChapterSearchCommand */ }
}
```

**Why not `ChapterDownloadFailedEvent` directly:** `MangaBlocklistService.Handle(ChapterDownloadFailedEvent)` (Plan 06-04) and a hypothetical `AutoRetryOrchestrator.Handle(ChapterDownloadFailedEvent)` would both subscribe to the SAME event. Sonarr's `IEventAggregator.PublishEvent` fans handlers out synchronously, but **handler order on a single event is non-deterministic** — DryIoc resolves handlers in registration order, but the registration order is enumeration-dependent.

The race: if `AutoRetryOrchestrator.Handle` runs BEFORE `MangaBlocklistService.Handle`, the `ChapterSearchCommand` is queued AGAINST a repository state that does not yet contain the just-failed release's blocklist row. `BlocklistSpecification.IsSatisfiedBy` returns Accept, and the same release gets re-grabbed → fails again → blocklisted again → loops until `MaxAutoRetriesPerChapter` saves us.

The fix: subscribe to `MangaBlocklistAddedEvent` instead. Plan 06-04's `MangaBlocklistService` enforces the ORDERING INVARIANT — `_repository.Insert(blocklist)` runs FIRST, then `_eventAggregator.PublishEvent(new MangaBlocklistAddedEvent(blocklist, message))`. Synchronous fan-out guarantees the row is COMMITTED when this handler runs. `BlocklistSpecification` correctly rejects the just-blocklisted release on the next decision pass; the next-best ranked release is grabbed.

The `AutoRetryOrchestratorFixture.Subscribes_to_MangaBlocklistAddedEvent_not_ChapterDownloadFailedEvent_ANTI_RACE_GATE` test asserts via runtime reflection that the IHandle interface for `MangaBlocklistAddedEvent` IS implemented AND the IHandle interface for `ChapterDownloadFailedEvent` is NOT implemented.

### Bounded auto-retry budget (D-13 + Pitfall 5)

```csharp
var failureCount = _historyService.FindByChapterId(chapterId)
    .Count(h => h.EventType == ChapterHistoryEventType.DownloadFailed);
var max = _configService.MaxAutoRetriesPerChapter;  // default 3 (D-13)
if (failureCount >= max) { /* exhausted — log Info + return */ }
else { _commandQueueManager.Push(new ChapterSearchCommand(...)); }
```

Without the bound, a degenerate case (every ranked release for a chapter blocklisted by Pitfall 5 mitigation in `MangaBlocklistService`) could loop forever. **Bounded N is the safety floor** — after N exhausted, the chapter sits in History as `DownloadFailed`; the user manually retries from the History row (HISTORY-03 path).

### BL-01 GUARD (cross-domain ID-collision)

`AutoRetryOrchestrator` queries `IChapterHistoryService.FindByChapterId(chapterId)` — manga sibling table. **Never** `IHistoryService.FindByEpisodeId(...)` — `Episode.Id` and `Chapter.Id` are independent SQLite autoincrement spaces; collision between any chapter and any unrelated TV episode would silently pollute the count. This is the same BL-01 fix applied at `AlreadyImportedSpecification` (Plan 06-03 / Plan 06-07).

### Idempotency in ProcessOne

`ProcessMangaCompletedDownloads.ProcessOne` is the convergence point for both reactive and poll paths. It must be safely callable multiple times for the same chapter id:

1. **ChapterFile presence check** — short-circuit if `_chapterFileService.GetFilesByChapter(chapterId).Any()`. Pattern 1 idempotency contract.
2. **Staging path guard** — if the staging file no longer exists (e.g., poll fires after a previous import already moved + cleaned up), short-circuit with Warn.
3. **Manga / Chapter resolution** — both `_chapterService.GetChapter` and `_mangaService.GetManga` short-circuit on null (with Warn) so the orchestrator never propagates resolution failures into `ImportApprovedChapters`.
4. **Decision-maker rejection** — leaves the `ChapterDownloadState` row in place per Q-8 reconciliation (Phase 4 housekeeper owns retention sweep).
5. **On import success only** — delete the state row + scratch dir. The state row delete is also idempotent inside `IChapterDownloadStateRepository.DeleteByChapterId` (silent no-op on no-match).

### Q-8 reconciliation — failure retention vs auto-cleanup

When the manga import-spec set rejects a decision, `ProcessOne` returns WITHOUT deleting the `ChapterDownloadState` row. Phase 4's `HousekeepInProcessDownloadsCommand` (`ChapterDownloadHousekeeper`) owns the retention sweep based on `Config.RetentionDays` (default 7). This avoids two cleanup paths racing on the same row and lets the user manually retry from a Wanted/History view within the retention window.

### Scratch-dir cleanup as best-effort (Pitfall 4 mitigation)

After a successful import, `ProcessMangaCompletedDownloads` deletes the scratch directory containing the page artifacts:

```csharp
try { _diskProvider.DeleteFolder(scratchDir, recursive: true); }
catch (Exception ex) { _logger.Warn(ex, "Could not delete scratch dir {0}", scratchDir); }
```

A failure here (read-only filesystem, permission glitch, file-locked) MUST NOT poison the import result — the import is logically complete. The `Scratch_dir_delete_failure_does_not_poison_import_result` fixture asserts `IImportApprovedChapters.Import` was still invoked once even when `DeleteFolder` throws.

## Manga Adaptation Notes

### TV-vs-manga mapping

| TV (Sonarr) | Manga (Mangarr) | Difference |
|-------------|-----------------|------------|
| `CompletedDownloadService.Check(TrackedDownload)` (poll-driven via `CheckForFinishedDownloadCommand`) | `ProcessMangaCompletedDownloads.Handle(ChapterArchivedEvent)` (reactive) + `Execute(ProcessMangaCompletedCommand)` (poll) | Manga gets BOTH paths (Pattern 1); TV is poll-only |
| `CompletedDownloadService` dispatches `IDownloadedEpisodesImportService.ProcessRootFolder` | `ProcessMangaCompletedDownloads` dispatches `IImportApprovedChapters.Import` | Plan 06-07 deliverable |
| TV's `Protocol == DownloadProtocol.Http` early-return at `CompletedDownloadService.cs:74-77` (Phase 4 D-10) | `ProcessMangaCompletedDownloads` is the manga-protocol handler the TV early-return defers to | Phase 8 collapse drops the early-return when `ImportApprovedEpisodes` deletes |
| TV uses `IHistoryService.FindByDownloadId` to correlate grab → import | Manga uses `IChapterHistoryService.FindByChapterId` (BL-01 fix) for the auto-retry budget count | Cross-domain ID collision class of bug |
| TV has no `AutoRetryOrchestrator` peer (`RedownloadFailedDownloadService.cs` + `RedownloadFailedSettings.cs` are TV's nearest analog but use a per-attempt timer not a bounded count) | Manga ships `AutoRetryOrchestrator` with explicit `MaxAutoRetriesPerChapter` budget (D-13) | No exact peer; D-12/D-13 is manga-shaped |

### Phase 8 collapse plan

When `Tv/` deletes (Phase 8 milestone), this directory collapses with `src/NzbDrone.Core/Download/`:

1. `ProcessMangaCompletedDownloads` → renames to canonical `ProcessCompletedDownloads` (or whatever the rename target is); the Pattern 1 hybrid event-handler + poller stays as the canonical shape.
2. `ProcessMangaCompletedCommand` → renames; `TaskManager.defaultTasks` registration stays as the canonical entry.
3. `AutoRetryOrchestrator` → stays as-is — no TV peer to collapse with. The `MangaBlocklistAddedEvent` subscription becomes the canonical post-Insert event hook (TV `Blocklist` may gain a similar event during the rename).
4. The `Protocol == DownloadProtocol.Http` early-return guard at TV `CompletedDownloadService.Check` lines 74-77 is dropped when `ImportApprovedEpisodes` deletes (only `ImportApprovedChapters` will remain).
5. Drop the `// Sonarr divergence: ...` headers from all files in this directory — they're no longer divergent.

## Cross-References

- Phase 4 contract: [src/NzbDrone.Core/MediaFiles/ChapterArchiving/ChapterArchivedEvent.cs](../../MediaFiles/ChapterArchiving/ChapterArchivedEvent.cs), [src/NzbDrone.Core/MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs](../../MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs), [src/NzbDrone.Core/Download/Clients/InProcess/ChapterDownloadState.cs](../Clients/InProcess/ChapterDownloadState.cs), [src/NzbDrone.Core/Download/Clients/InProcess/IChapterDownloadStateRepository.cs](../Clients/InProcess/IChapterDownloadStateRepository.cs)
- Plan 06-04 ordering contract: [src/NzbDrone.Core/Blocklisting/Manga/MangaBlocklistAddedEvent.cs](../../Blocklisting/Manga/MangaBlocklistAddedEvent.cs), [src/NzbDrone.Core/Blocklisting/Manga/MangaBlocklistService.cs](../../Blocklisting/Manga/MangaBlocklistService.cs) (Insert FIRST, then PublishEvent)
- Plan 06-03 history substrate: [src/NzbDrone.Core/History/Manga/IChapterHistoryService.cs](../../History/Manga/IChapterHistoryService.cs), [src/NzbDrone.Core/History/Manga/ChapterHistoryEventType.cs](../../History/Manga/ChapterHistoryEventType.cs)
- Plan 06-06 search command: [src/NzbDrone.Core/IndexerSearch/Manga/ChapterSearchCommand.cs](../../IndexerSearch/Manga/ChapterSearchCommand.cs)
- Plan 06-07 import pipeline: [src/NzbDrone.Core/MediaFiles/MangaImport/IImportApprovedChapters.cs](../../MediaFiles/MangaImport/IImportApprovedChapters.cs), [src/NzbDrone.Core/MediaFiles/MangaImport/MangaImportDecisionMaker.cs](../../MediaFiles/MangaImport/MangaImportDecisionMaker.cs)
- TV side (the Sonarr peer this manga handler was modelled on): `NzbDrone.Core/Download/CompletedDownloadService.cs` (Phase 4 D-10 early-return guard for Protocol=Http) — DELETED in the Phase 15 `Tv/` removal; cited for provenance only, absent at HEAD (path shown repo-relative-from-`src/` since it no longer resolves).
- TaskManager registration: [src/NzbDrone.Core/Jobs/TaskManager.cs](../../Jobs/TaskManager.cs) — `defaultTasks` block contains the `ProcessMangaCompletedCommand` entry
- Anti-pattern C skill: [.claude/skills/sonarr-consistency-audit/SKILL.md](../../../../.claude/skills/sonarr-consistency-audit/SKILL.md)
- Tests: [src/NzbDrone.Core.Test/Download/Manga/](../../../NzbDrone.Core.Test/Download/Manga/) — `ProcessMangaCompletedDownloadsFixture` (11 tests) + `AutoRetryOrchestratorFixture` (8 tests)
- Plan: [.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-08-PLAN.md](../../../../.planning/phases/06-pipeline-wanted-history-blocklist-reader-notify/06-08-PLAN.md)
