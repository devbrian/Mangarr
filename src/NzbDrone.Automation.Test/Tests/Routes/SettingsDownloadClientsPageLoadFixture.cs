using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/downloadclients` renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsDownloadClientsPage (Phase 18).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsDownloadClientsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_downloadclients_settings()
    {
        var settings = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/downloadclients$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
