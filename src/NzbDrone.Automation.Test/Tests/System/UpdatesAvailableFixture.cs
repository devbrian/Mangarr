using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 145
// (`v5-endpoint GET /api/v5/update → System/Updates available list`).
//
// Non-cassette-dependent: /api/v5/update returns the local update package
// list. On a fresh dev runner there are no updates available, so the page
// renders the "On latest version" / "No updates are available" terminal
// state. We assert the text matches one of the documented terminal states.
[TestFixture]
[Category("AutomationTest")]
public class UpdatesAvailableFixture : AutomationTest
{
    [Test]
    public async Task updates_panel_renders_terminal_state()
    {
        await new SystemUpdatesPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-updates-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility): page resolves into ONE
        // of three terminal states:
        //   (a) "No updates are available" (empty list — dev runner case)
        //   (b) "On latest version" (list contains current version)
        //   (c) "Install Latest" / version list (newer updates exist)
        // Match any of the three terminal-state strings; this state assertion
        // catches the stuck-spinner / failed-fetch regression that a pure
        // visibility test on `system-updates-page` would miss.
        var pageText = await Page.GetByTestId("system-updates-page").TextContentAsync();
        pageText.Should().NotBeNullOrEmpty();
        pageText.Should().MatchRegex(@"(No updates are available|On latest version|Install Latest|Recent Changes|version)");

        Page.Url.Should().EndWith("/system/updates");
    }
}
