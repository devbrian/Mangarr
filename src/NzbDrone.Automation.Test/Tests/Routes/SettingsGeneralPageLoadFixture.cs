using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/general` renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsGeneralPage (Phase 18 — canonical 49-LOC analog).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsGeneralPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_general_settings()
    {
        var settings = await new SettingsGeneralPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/general$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
