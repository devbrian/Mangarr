using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — UI settings form-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/settings/ui | Settings/UI form load`.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class UiConfigFixture : AutomationTest
{
    [Test]
    public async Task form_loads()
    {
        var page = await new SettingsUIPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Flake-proofing (getResponseBody race, same class as PR #287 / run 26579906231):
        // read the config via the buffered Page.APIRequest.GetAsync (body decoupled from page
        // navigation) instead of WaitForResponseAsync + ReloadAsync, whose captured response
        // body the reload can evict ("No resource with given identifier found"). Mirrors
        // MediaManagementConfigFixture (Phase 20 LEARNINGS SL-2). "theme" is a JSON field name
        // (not visible DOM text), so the API body — not a DOM check — is the contract; the
        // page-open + PageContainer assertion above already covers form render.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/settings/ui",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("theme");
    }
}
