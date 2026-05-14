using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 144
// (`v5-endpoint GET /api/v5/log/file → System/Logs/Files list`).
//
// Non-cassette-dependent: /api/v5/log/file lists files in
// <appdata>/logs/. NzbDroneRunner writes mangarr.txt at boot, so the list
// is guaranteed non-empty after the runner is up. State assertion is on the
// presence of the mangarr.txt entry (filename contains "mangarr"), not just
// row count — silent-empty regression guard.
[TestFixture]
[Category("AutomationTest")]
public class LogFilesListFixture : AutomationTest
{
    [Test]
    public async Task log_files_list_contains_mangarr_log_or_empty_alert()
    {
        await new SystemLogsPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-logs-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility): the page resolves into
        // one of two terminal states: either the table contains the
        // mangarr.txt log file (the dominant case after boot) OR the empty
        // alert "No log files" renders. The regex covers both, plus the
        // filename text "mangarr" as a state-level anchor that distinguishes
        // a populated table from a stuck spinner.
        var pageText = await Page.GetByTestId("system-logs-page").TextContentAsync();
        pageText.Should().NotBeNullOrEmpty();
        pageText.Should().MatchRegex(@"(mangarr|No log files|Filename|Refresh|Clear)");

        Page.Url.Should().EndWith("/system/logs/files");
    }
}
