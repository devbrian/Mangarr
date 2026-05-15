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
        //
        // We scope to the FieldSet (legend "Health") rather than the whole
        // `system-status-page` testid. The page wrapper concatenates Health +
        // DiskSpace + About + MoreInfo into one text run with no separator —
        // matching the regex on that blob is meaningless because tokens
        // like "Warning" / "Error" elsewhere on the page would falsely satisfy
        // a Health-specific contract.
        //
        // WR-09 (18-REVIEW): "Test All" / "Wiki" / "Health" are always-present
        // toolbar/page-title tokens. Anchor only on data-bearing alternatives
        // (empty-state message OR a populated row indicator). A populated
        // health table renders the issue severity label ("Warning" /
        // "Error" / "Info") and the source string — at least one of those
        // text strings will be present when there is an issue.
        var healthSection = Page.Locator("fieldset:has(legend:has-text('Health'))");
        await healthSection.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var healthText = await healthSection.TextContentAsync();
        healthText.Should().NotBeNullOrEmpty();
        healthText.Should().MatchRegex(@"(No issues with your configuration|Warning|Notice|Error)");

        // URL stability — confirms no spurious nav and serves as the
        // shell-level state anchor.
        Page.Url.Should().EndWith("/system/status");
    }
}
