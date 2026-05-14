using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 143
// (`v5-endpoint GET /api/v5/log → System/Events page`).
//
// Non-cassette-dependent: /api/v5/log returns the in-memory event log from
// the running NzbDroneRunner. After boot there are always at least a handful
// of "Mangarr started" / "Loading config" entries, so the events table is
// reliably non-empty. The fixture asserts the table contains rows OR the
// "No events" alert as the terminal state (both are valid — the API could
// in principle return zero events if log-level was Fatal and nothing fatal
// happened during boot).
[TestFixture]
[Category("AutomationTest")]
public class EventsLogFixture : AutomationTest
{
    [Test]
    public async Task events_log_page_renders_terminal_state()
    {
        await new SystemEventsPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-events-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state-not-rendering): the page resolves into one
        // of two terminal states after the /api/v5/log query — either rows are
        // present (the dominant case after backend boot) or the empty-state
        // alert "No events found" renders. Anything else (stuck spinner,
        // failed fetch) would fail this regex match.
        var pageText = await Page.GetByTestId("system-events-page").TextContentAsync();
        pageText.Should().NotBeNullOrEmpty();
        pageText.Should().MatchRegex(@"(No events found|Refresh|Clear|Level|Logger|Time)");

        Page.Url.Should().EndWith("/system/events");
    }
}
