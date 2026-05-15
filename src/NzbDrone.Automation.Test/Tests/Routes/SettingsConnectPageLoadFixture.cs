using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/connect` renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsNotificationsPage (Phase 18 — historic 'connect' slug).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsConnectPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_connect_settings()
    {
        var settings = await new SettingsNotificationsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/connect$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
