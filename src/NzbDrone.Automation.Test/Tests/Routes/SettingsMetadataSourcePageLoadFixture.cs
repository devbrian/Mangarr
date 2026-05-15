using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/metadatasource` renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsMetadataSourcePage (Phase 18).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsMetadataSourcePageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_metadatasource_settings()
    {
        var settings = await new SettingsMetadataSourcePage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/metadatasource$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
