using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/tags` renders.
// D-04: route axis ⇒ PR-smoke.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsTagsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_tags_settings()
    {
        var settings = await new SettingsTagsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/tags$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
