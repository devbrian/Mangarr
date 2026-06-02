using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.JobTests
{
    // Phase 6 Plan 06-06 Task 3 — Anti-pattern C check (sonarr-consistency-audit
    // SKILL.md):
    //   * MangaRssSyncCommand + MissingChapterSearchCommand MUST be registered
    //     in TaskManager.defaultTasks at runtime — NOT seeded via 001
    //     Insert.IntoTable.
    //   * 001_mangarr_baseline.cs MUST contain ZERO Insert.IntoTable calls
    //     (modulo commented-out documentation lines).
    //
    // This is a textual fixture rather than a behavioral one because the bug
    // class it guards against (Phase 2 RefreshMangaCommand seeded via migration)
    // would have a green behavioral test (the row exists in the DB) yet a
    // structurally-wrong production setup. The fixture asserts the registration
    // mechanism, not the runtime outcome.
    [TestFixture]
    public class TaskManagerDefaultTasksFixture : TestBase
    {
        private string _taskManagerSource;
        private string _migration001Source;

        // The defaultTasks initializer block only — sliced between
        // `var defaultTasks = new List<ScheduledTask>` and `var currentTasks =`. Scoping the
        // RefreshMonitored/ProcessMonitored assertions to this block (instead of a file-wide
        // string match) catches a cadence regression and avoids a false-green on a stray comment
        // elsewhere in TaskManager.cs that merely names the command.
        private string _defaultTasksBlock;

        [SetUp]
        public void Setup()
        {
            // Anti-pattern C is a TEXTUAL guard — it reads the production .cs files and
            // asserts on their content, so it needs the source tree on disk. That holds for
            // every dev build and for any CI job that does a full checkout, but NOT for the
            // unit_test job, which runs from a stripped, standalone test artifact (no src/).
            // A hard-coded "../.." hop is also brittle: it is only correct for the local
            // <repo>/_tests/net10.0 layout. Walk upward from TestDirectory looking for the
            // src/NzbDrone.Core marker; if the source tree is not reachable, the structural
            // guard cannot run here — mark inconclusive rather than failing the build. The
            // guard still hard-runs locally and in checkout-based CI jobs.
            var srcRoot = FindCoreSourceRoot(TestContext.CurrentContext.TestDirectory);
            if (srcRoot == null)
            {
                Assert.Inconclusive(
                    "src/NzbDrone.Core is not reachable from the test directory — the "
                    + "sonarr-consistency-audit Anti-pattern C textual guard runs in dev builds "
                    + "and checkout-based CI jobs, not from a standalone test artifact.");
            }

            _taskManagerSource = File.ReadAllText(Path.Combine(srcRoot, "Jobs", "TaskManager.cs"));
            _migration001Source = File.ReadAllText(Path.Combine(srcRoot, "Datastore", "Migration", "001_mangarr_baseline.cs"));

            // Slice the defaultTasks initializer: from `var defaultTasks = new List<ScheduledTask>`
            // up to (not including) the next `var currentTasks =`. This is the registration list
            // proper — comments and other code outside it must not satisfy the scoped assertions.
            var blockStart = _taskManagerSource.IndexOf("var defaultTasks = new List<ScheduledTask>");
            blockStart.Should().BeGreaterThan(0, "TaskManager.cs must declare a defaultTasks List<ScheduledTask> initializer");
            var blockEnd = _taskManagerSource.IndexOf("var currentTasks =", blockStart);
            blockEnd.Should().BeGreaterThan(blockStart, "the defaultTasks initializer must be followed by `var currentTasks =`");
            _defaultTasksBlock = _taskManagerSource.Substring(blockStart, blockEnd - blockStart);
        }

        // Walk up the directory chain from startDir, returning the first ancestor's
        // src/NzbDrone.Core that actually contains Jobs/TaskManager.cs — or null if the
        // source tree is not present in this layout.
        private static string FindCoreSourceRoot(string startDir)
        {
            for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "NzbDrone.Core");
                if (File.Exists(Path.Combine(candidate, "Jobs", "TaskManager.cs")))
                {
                    return candidate;
                }
            }

            return null;
        }

        [Test]
        public void TaskManager_defaultTasks_registers_MangaRssSyncCommand()
        {
            _taskManagerSource.Should().Contain("typeof(MangaRssSyncCommand).FullName",
                "Plan 06-06 Task 3 must register MangaRssSyncCommand in TaskManager.defaultTasks (Anti-pattern C: NOT via migration seed)");
        }

        [Test]
        public void TaskManager_defaultTasks_registers_MissingChapterSearchCommand()
        {
            _taskManagerSource.Should().Contain("typeof(MissingChapterSearchCommand).FullName",
                "Plan 06-06 Task 3 must register MissingChapterSearchCommand in TaskManager.defaultTasks (Anti-pattern C: NOT via migration seed)");
        }

        [Test]
        public void TaskManager_defaultTasks_registers_ProcessMangaCompletedCommand()
        {
            // Plan 06-08 Task 1 — RESEARCH Pattern 1 hybrid event-handler + 1-min poller.
            // The poller path runs ProcessMangaCompletedDownloads.Execute periodically as a
            // resilience cover for missed ChapterArchivedEvent dispatches.
            _taskManagerSource.Should().Contain("typeof(ProcessMangaCompletedCommand).FullName",
                "Plan 06-08 Task 1 must register ProcessMangaCompletedCommand in TaskManager.defaultTasks (Anti-pattern C: NOT via migration seed)");
        }

        [Test]
        public void TaskManager_defaultTasks_registers_RefreshMonitoredMangaDownloadsCommand_with_one_minute_cadence()
        {
            // Phase 36 Plan 05 Task 1 (LOOP-01 / LOOP-05) — 1-min monitoring-loop poll heart.
            // MangaDownloadMonitoringService.Execute polls every DownloadHandlingEnabled() client,
            // tracks each item, runs the Completed/Failed Checks, and publishes
            // TrackedDownloadRefreshedEvent as the LAST step (the dead-queue fix). Scoped to the
            // defaultTasks block AND asserts the Interval = 1 cadence (a cadence regression — e.g.
            // someone bumping it to 60 — would slip past a file-wide typeof() match).
            _defaultTasksBlock.Should().Contain("typeof(RefreshMonitoredMangaDownloadsCommand).FullName",
                "Plan 36-05 Task 1 must register RefreshMonitoredMangaDownloadsCommand in the TaskManager.defaultTasks block (Anti-pattern C: NOT via migration seed)");

            // The registration's ScheduledTask carries Interval = 1. Assert the cadence literal
            // appears immediately above the TypeName within a single ScheduledTask initializer by
            // matching the `Interval = 1, ... TypeName = typeof(RefreshMonitoredMangaDownloadsCommand)`
            // shape within the block (regex spans the intervening newline/whitespace).
            _defaultTasksBlock.Should().MatchRegex(
                @"Interval\s*=\s*1\s*,\s*TypeName\s*=\s*typeof\(RefreshMonitoredMangaDownloadsCommand\)\.FullName",
                "Plan 36-05 Task 1 — RefreshMonitoredMangaDownloadsCommand must be registered at the 1-minute monitoring cadence (Interval = 1)");
        }

        [Test]
        public void TaskManager_defaultTasks_does_NOT_register_ProcessMonitoredMangaDownloadsCommand()
        {
            // Phase 36 Plan 05 Task 1 (Q-poll resolution) — ProcessMonitoredMangaDownloadsCommand is
            // QUEUED-ONLY: the MangaDownloadMonitoringService pushes it onto the command queue at the
            // tail of every Refresh(). It must NEVER appear in the defaultTasks block (no scheduled
            // cadence — the monitor owns its dispatch). Scoped to the block so the explanatory comment
            // in TaskManager.cs that NAMES the command (documenting the queued-only contract) does not
            // produce a false failure — a file-wide NotContain would trip on that comment.
            _defaultTasksBlock.Should().NotContain("typeof(ProcessMonitoredMangaDownloadsCommand).FullName",
                "Plan 36-05 Task 1 — ProcessMonitoredMangaDownloadsCommand is queued-only; it must NOT be registered in the TaskManager.defaultTasks block (it is pushed at the tail of Refresh()).");
        }

        [Test]
        public void TaskManager_HandleAsync_ConfigSavedEvent_rebroadcasts_MangaRssSyncCommand()
        {
            // Phase 6 Plan 14 — WR-02. The handler at TaskManager.cs HandleAsync(ConfigSavedEvent)
            // must rebroadcast MangaRssSyncCommand interval changes so users do not need to
            // restart for Settings → MangaRssSyncInterval changes to apply. Pure-textual
            // assertion — the runtime path is exercised by TaskManagerFixture in upstream tests;
            // this fixture guards the registration shape.
            var handlerStart = _taskManagerSource.IndexOf("public void HandleAsync(ConfigSavedEvent");
            handlerStart.Should().BeGreaterThan(0, "HandleAsync(ConfigSavedEvent) must exist in TaskManager");

            // Slice the file from the handler signature to the next top-level closing brace
            // (heuristic: search forward for "\n        }" — 8-space indented closer).
            var handlerEnd = _taskManagerSource.IndexOf("\n        }", handlerStart);
            handlerEnd.Should().BeGreaterThan(handlerStart, "HandleAsync(ConfigSavedEvent) closing brace not found");

            var handlerBody = _taskManagerSource.Substring(handlerStart, handlerEnd - handlerStart);
            handlerBody.Should().Contain("typeof(MangaRssSyncCommand)",
                "Plan 14 WR-02 must add MangaRssSyncCommand to the rebroadcast set in HandleAsync(ConfigSavedEvent).");
            handlerBody.Should().Contain("GetMangaRssSyncInterval()",
                "Plan 14 WR-02 must call GetMangaRssSyncInterval() to refresh the manga RSS interval.");
        }

        [Test]
        public void Migration_001_contains_zero_Insert_IntoTable_calls()
        {
            // Strip comments before counting — a `// ... Insert.IntoTable ...` documentation
            // note must not trip the gate. Count only non-commented occurrences.
            var nonCommentLines = _migration001Source.Split('\n');
            var hits = 0;
            foreach (var raw in nonCommentLines)
            {
                var trimmed = raw.TrimStart();
                if (trimmed.StartsWith("//"))
                {
                    continue;
                }

                if (trimmed.Contains("Insert.IntoTable"))
                {
                    hits++;
                }
            }

            hits.Should().Be(0, "001_mangarr_baseline.cs must not seed scheduled-task rows (sonarr-consistency-audit Anti-pattern C; Phase 2 retro)");
        }
    }
}
