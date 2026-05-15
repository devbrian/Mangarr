using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings/profiles` page renders.
// D-04: route axis ⇒ PR-smoke.
// REUSES existing SettingsTranslationProfilesPage (Phase 18 — manga-canonical Profiles editor).
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsProfilesPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_profiles_settings()
    {
        var settings = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);

        Page.Url.Should().MatchRegex(@"/settings/profiles$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        settings.Should().NotBeNull();
    }
}
