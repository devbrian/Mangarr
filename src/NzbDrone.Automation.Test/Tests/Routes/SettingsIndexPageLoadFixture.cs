using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 20 Plan 20-02 — INVENTORY route-axis row: `/settings` index page renders.
// D-04: route axis ⇒ PR-smoke.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SettingsIndexPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_settings_index()
    {
        var settings = await new SettingsIndexPage(Page).OpenAsync(RootUri);

        // PageLoadFixture filename exemption per scripts/audit-test-assertions.sh:61 —
        // route-axis smokes assert URL match + page container visibility.
        Page.Url.Should().MatchRegex(@"/settings/?$");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        // Reference settings to keep the fluent return-this contract live (D-17).
        settings.Should().NotBeNull();
    }
}
