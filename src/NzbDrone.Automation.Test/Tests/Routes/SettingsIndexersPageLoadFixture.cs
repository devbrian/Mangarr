using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/indexers` renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsIndexersPage (Phase 18).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsIndexersPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_indexers_settings()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/indexers$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
