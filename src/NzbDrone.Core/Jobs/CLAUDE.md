# NzbDrone.Core/Jobs

## Purpose

The **scheduled-task substrate**. Registers periodic commands (RSS sync, refresh, housekeeping, manga RSS poll, missing-chapter sweep, etc.) with `Scheduler` for interval-based dispatch via `CommandQueueManager.Push`.

This is **infrastructure** — media-agnostic. The directory survives the Phase 14 `Tv/` cutover unchanged.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Jobs\`

## Key Files

| File | Purpose |
|------|---------|
| `TaskManager.cs` | Owns `defaultTasks` registration. `Handle(ApplicationStartedEvent)` seeds the schedule list at startup, reconciling against `_scheduledTaskRepository.All()` (deletes rows whose `TypeName` no longer appears in code, upserts new rows for code-side additions). `HandleAsync(ConfigSavedEvent)` rebroadcasts cadence-changed commands (e.g. `MangaRssSyncCommand` per Phase 6 Plan 14 WR-02 mitigation, `RssSyncCommand`, `BackupCommand`) so Settings changes apply without restart. `Handle(CommandExecutedEvent)` updates `LastExecution` + `LastStartTime` for the row matching the executed command's `TypeName`. |
| `Scheduler.cs` | Thread-loop: every minute, walks `_taskManager.GetPending()`; for each task whose `LastExecution + Interval < now`, pushes the typed command via `_commandQueueManager.Push(commandFromTypeName(task.TypeName))`. Subscribes to `ApplicationStartedEvent` (start the timer) + `ApplicationShutdownRequested` (stop the timer; cancel pending). |
| `ScheduledTaskRepository.cs` | Dapper repository (`IScheduledTaskRepository`) over the `ScheduledTasks` table (Migration 001). `GetDefinition(Type)` resolves a row by `TypeName`. `SetLastExecutionTime(id, executionTime, startTime)` updates run timestamps after each successful command execution. |
| `ScheduledTask.cs` | POCO row inheriting `ModelBase`. Fields: `TypeName` (full assembly-qualified name), `Interval` (minutes), `LastExecution`, `LastStartTime`, `Priority` (`CommandPriority` enum, default `Low`). |

## Patterns / Conventions

### Anti-pattern C — `defaultTasks` vs migration seed (canonical Mangarr-consistency-audit gate)

Scheduled commands MUST be registered in `TaskManager.defaultTasks` at runtime (in the C# `List<ScheduledTask>` initializer inside `Handle(ApplicationStartedEvent)`), NOT seeded via Migration 001 `Insert.IntoTable("ScheduledTasks")`. The structural fixture [`TaskManagerDefaultTasksFixture`](../../NzbDrone.Core.Test/JobTests/TaskManagerDefaultTasksFixture.cs) enforces both halves:

- **(a)** per-command "registers X" assertions — textual `_taskManagerSource.Should().Contain("typeof(NewMangaCommand).FullName", ...)` checks against the `TaskManager.cs` source file
- **(b)** `Migration_001_contains_zero_Insert_IntoTable_calls` floor — asserts the absence of the wrong pattern in [`Datastore/Migration/001_mangarr_baseline.cs`](../Datastore/Migration/001_mangarr_baseline.cs)

The bug class was first surfaced in Phase 6 — Plan 06-06 wired `MangaRssSyncCommand` + `MissingChapterSearchCommand` runtime registrations after they had been silently shipped without cadence; Plan 06-08 wired `ProcessMangaCompletedCommand` (since RETIRED in Phase 39 Plan 39-01 with the in-process download vertical — see Manga Adaptation Notes below). The [`sonarr-consistency-audit` skill](../../../.claude/skills/sonarr-consistency-audit/SKILL.md) was authored to catch this exact pattern in future phases. Phase 11 swept the full inventory (16 registrations as of 2026-05-06) and verified zero gaps.

### Standard registration pattern

```csharp
// 4-line preamble: phase + decision + behavior + Anti-pattern C disclaimer
// Phase 6 D-09 — daily Wanted/Missing sweep. Walks monitored Mangas with monitored
// unmet chapters; groups by MangaId; pushes one MangaSearchCommand per Manga.
// Registered at runtime via TaskManager.defaultTasks per sonarr-consistency-audit
// anti-pattern C (NOT seeded via 001 Insert.IntoTable).
new ScheduledTask
{
    Interval = 24 * 60,                                       // minutes
    TypeName = typeof(MissingChapterSearchCommand).FullName   // full assembly-qualified name
}
```

### Sister fixture row (mandatory)

Every new `defaultTasks` row MUST get a sister fixture row in `TaskManagerDefaultTasksFixture.cs`:

```csharp
[Test]
public void TaskManager_defaultTasks_registers_NewMangaCommand()
{
    _taskManagerSource.Should().Contain("typeof(NewMangaCommand).FullName",
        "Plan NN-NN must register NewMangaCommand in TaskManager.defaultTasks (Anti-pattern C: NOT via migration seed)");
}
```

Test-class base is `TestBase` (NOT `CoreTest<T>` — this is a textual assertion fixture, not a behavioral one). The `_taskManagerSource` field is loaded in `[SetUp]` via `File.ReadAllText` against an absolute repo path resolved from `TestContext.CurrentContext.TestDirectory`.

## Manga Adaptation Notes

Phase 6 Plan 06-06 added the manga registrations: `MangaRssSyncCommand` (interval-configurable via `Config.MangaRssSyncInterval`, default 15 min), `MissingChapterSearchCommand` (24 h). Phase 2 D-18 added `RefreshMangaCommand` (12 h, mirrors `RefreshSeriesCommand`).

**Phase 39 (Plan 39-01) — two in-process `defaultTasks` rows STRIPPED.** `ProcessMangaCompletedCommand` (the 1-min completion-poll resilience trigger for the reactive `IHandle<ChapterArchivedEvent>` in-process path) and `HousekeepInProcessDownloadsCommand` (the 24-h in-process downloader retention/scratch sweep) were both removed from `TaskManager.defaultTasks` along with their commands when the in-process download vertical was retired. `TaskManager.Handle(ApplicationStartedEvent)` self-reconciles the two orphaned `ScheduledTasks` DB rows on next start (deletes rows whose `TypeName` no longer appears in code) — no migration needed for the scheduled-task rows. The gateway-download monitoring loop (Phase 36; `RefreshMonitoredMangaDownloadsCommand` / `ProcessMonitoredMangaDownloadsCommand` under `Download/Manga/`) replaces the retired in-process completion poll.

Phase 11 swept the full inventory (16 registrations as of 2026-05-06 — see [11-COMMANDS-AND-EXECUTE-FINDINGS.md](../../../.planning/phases/11-commands-and-execute-sweep/11-COMMANDS-AND-EXECUTE-FINDINGS.md) Axis 2) and confirmed zero Axis-2 (cadence) gaps and zero Anti-pattern C regressions.

`TaskManager` survives Phase 14 unchanged — the registration list narrows when `Tv/` deletes (`RefreshSeriesCommand`, `RssSyncCommand`, `UpdateSceneMappingCommand`, etc. drop out), but the substrate itself is media-agnostic.

## Cross-References

- [Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — Command/IExecute contract; Anti-pattern C + D documentation
- [Datastore/Migration/CLAUDE.md](../Datastore/Migration/CLAUDE.md) — Migration 001 `ScheduledTasks` table create + zero-seed floor
- [TaskManagerDefaultTasksFixture.cs](../../NzbDrone.Core.Test/JobTests/TaskManagerDefaultTasksFixture.cs) — Anti-pattern C structural gate
- [.planning/phases/11-commands-and-execute-sweep/11-COMMANDS-AND-EXECUTE-FINDINGS.md](../../../.planning/phases/11-commands-and-execute-sweep/11-COMMANDS-AND-EXECUTE-FINDINGS.md) — full Axis-1 (class+handler) + Axis-2 (cadence) inventory
