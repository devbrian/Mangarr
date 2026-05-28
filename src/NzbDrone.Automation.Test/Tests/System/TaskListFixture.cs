using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 140
// (`v5-endpoint GET /api/v5/system/task → System/Tasks page`).
//
// Non-cassette-dependent: /api/v5/system/task returns the scheduled-task list
// seeded by TaskManager.defaultTasks at boot. On any healthy runner there are
// >= 5 scheduled tasks (RefreshManga, BackupCommand, CheckForFinishedDownload,
// HousekeepingCommand, RssSyncCommand baseline). The fixture asserts that the
// table contains the canonical task names — this is a state assertion
// (text-content match), not a visibility-only test.
[TestFixture]
[Category("AutomationTest")]
public class TaskListFixture : AutomationTest
{
    [Test]
    public async Task scheduled_tasks_list_contains_baseline_task_names()
    {
        await new SystemTasksPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-tasks-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility, AUTO-WAITING): the
        // scheduled-task table MUST contain at least one baseline task label.
        // Refresh Manga (the Phase-2 manga-renamed RefreshSeries) is the most
        // stable anchor — it ships in every Mangarr boot regardless of
        // indexer/import-list config; Housekeeping / Rss Sync are alternates.
        //
        // This remains the silent-empty regression guard: an empty API array
        // still fails here (the column header alone won't match these labels).
        //
        // WR-09 (18-REVIEW): "Backup" appears in the side-nav entry (always
        // present), so anchor only on data-bearing labels that require the
        // scheduled-task table to actually have rows.
        //
        // Flake fix (run 26567511609, postgres-16 nightly leg — the other 3 legs
        // passed): the "Scheduled" table header renders BEFORE the
        // /api/v5/system/task round-trip populates the rows, so a one-shot
        // TextContentAsync()+MatchRegex raced the data and captured only the
        // column header ("ScheduledQueueNameQueuedStartedEndedDuration") with no
        // rows. Use the auto-retrying ToContainTextAsync(Regex) so the assertion
        // waits for the rows instead of reading once too early.
        await Assertions.Expect(Page.GetByTestId("system-tasks-page"))
            .ToContainTextAsync(
                new Regex(@"(Refresh Manga|Housekeeping|Rss Sync)"),
                new() { Timeout = 15000 });

        Page.Url.Should().EndWith("/system/tasks");
    }
}
