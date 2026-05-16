using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/customformatprofiles` renders.
// D-04: route axis ⇒ PR-smoke.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsCustomFormatProfilesPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_cfp_settings()
    {
        var settings = await new SettingsCustomFormatProfilesPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/customformatprofiles$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
