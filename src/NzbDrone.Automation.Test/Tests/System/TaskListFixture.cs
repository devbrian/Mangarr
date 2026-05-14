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

        // STATE assertion 2 (state, not visibility): the page body text MUST
        // contain at least one of the baseline task labels. RefreshManga (the
        // Phase-2 manga-renamed RefreshSeries) is the most stable anchor —
        // it ships in every Mangarr boot regardless of indexer/import-list
        // config. The "Backup" task is the second anchor.
        //
        // This is the silent-empty regression guard: if the API returned an
        // empty array, the visibility-only test would still pass on the
        // "Scheduled" FieldSet legend rendering. Asserting on the text of
        // an actual row prevents that.
        var pageText = await Page.GetByTestId("system-tasks-page").TextContentAsync();
        pageText.Should().NotBeNullOrEmpty();

        // WR-09 (18-REVIEW): "Backup" appears in the side-nav nav entry
        // (always present). Anchor only on data-bearing alternatives that
        // require the scheduled-task table to actually have rows.
        pageText.Should().MatchRegex(@"(Refresh Manga|Housekeeping|Rss Sync)");

        Page.Url.Should().EndWith("/system/tasks");
    }
}
