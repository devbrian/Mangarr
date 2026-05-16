using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — General settings form-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/settings/general | Settings/General form load`.
///
/// PR #173 CI-fix (2026-05-15 iteration 2): same Playwright protocol race as
/// TagListFixture / UpdateConfigFixture — reading body after reload navigation
/// retired the response resource ("No resource with given identifier found").
/// Switched to direct APIRequest.GetAsync (the stable pattern). Page navigation
/// preserved as a settings-page smoke trigger.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class GeneralConfigFixture : AutomationTest
{
    [Test]
    public async Task form_loads()
    {
        var page = await new SettingsGeneralPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/settings/general",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("bindAddress");
    }
}
