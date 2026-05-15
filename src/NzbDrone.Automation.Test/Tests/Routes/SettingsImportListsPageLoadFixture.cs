using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/importlists` renders.
// D-04: route axis ⇒ PR-smoke.
// v1.1+ placeholder per Phase 20 D-06 — page-load coverage only (no CRUD).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsImportListsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_importlists_settings()
    {
        var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/importlists$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
