using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/mediamanagement` form renders.
// D-04: route axis ⇒ PR-smoke.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsMediaManagementPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_mediamanagement_settings()
    {
        var settings = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/mediamanagement$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
