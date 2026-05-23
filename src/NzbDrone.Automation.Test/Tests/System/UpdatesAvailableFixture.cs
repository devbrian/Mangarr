using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 145
// (`v5-endpoint GET /api/v5/update → System/Updates available list`).
//
// Phase 29 DIST2-03 broker swap: `IUpdatePackageProvider` is now backed by
// `GitHubReleasesUpdatePackageProvider` (live HTTPS call to api.github.com),
// not the synchronous `NoOpUpdatePackageProvider` placeholder. The page now
// resolves through a real loading state — assertions MUST use Playwright's
// auto-waiting `ToContainTextAsync(Regex)` to poll past the LoadingIndicator,
// not a single `TextContentAsync()` snapshot (which races the API call and
// returns the empty LoadingIndicator wrapper).
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
        //   (d) "Update Available" banner (Phase 29 DIST2-03 broker — newer GH release exists)
        // Auto-waiting polls past the LoadingIndicator while
        // `GitHubReleasesUpdatePackageProvider` completes its live API call.
        await Assertions.Expect(Page.GetByTestId("system-updates-page"))
            .ToContainTextAsync(
                new Regex(@"No updates are available|On latest version|Install Latest|Recent Changes|Update Available|version"),
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        Page.Url.Should().EndWith("/system/updates");
    }
}
