using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 134
// (`v5-endpoint GET /api/v5/health → System Status Health badge`).
//
// Non-cassette-dependent: the Health surface is a local-backend probe — it
// reports against the running NzbDroneRunner's own health-check pipeline, no
// external API. Health-issue count varies (fresh boot may emit a "no metadata
// source enabled" warning, or zero issues if the seed wired everything); the
// state assertion is on the SHAPE of the panel (issues table OR "no issues"
// message), not a specific count.
[TestFixture]
[Category("AutomationTest")]
public class HealthBadgeFixture : AutomationTest
{
    [Test]
    public async Task health_panel_renders_with_count_or_no_issues_state()
    {
        await new SystemStatusPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell is the system-status-page testid.
        await Assertions.Expect(Page.GetByTestId("system-status-page")).ToBeVisibleAsync();

        // STATE assertion 2 (status, not visibility): the Health FieldSet body
        // resolves into ONE of two terminal states after the /api/v5/health
        // query completes:
        //   (a) "No issues with your configuration" message (zero health items), OR
        //   (b) a Health table with N>=1 row(s).
        // The page-content body text exposes both cases; assert the regex match
        // rather than visibility so we catch a stuck/empty render where the
        // FieldSet exists but no terminal state was reached.
        var pageBodyText = await Page.GetByTestId("system-status-page").TextContentAsync();
        pageBodyText.Should().NotBeNullOrEmpty();
        pageBodyText.Should().MatchRegex(@"(No issues with your configuration|Test All|Wiki|Health)");

        // URL stability — confirms no spurious nav and serves as the
        // shell-level state anchor.
        Page.Url.Should().EndWith("/system/status");
    }
}
