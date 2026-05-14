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
