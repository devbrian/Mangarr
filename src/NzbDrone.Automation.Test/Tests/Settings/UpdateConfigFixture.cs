using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — Update settings form-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/settings/update | Update settings`.
///
/// PR #173 CI-fix (2026-05-15): the previous "open /settings/general then wait
/// for GET /api/v5/settings/update" filter never matched — the
/// `/settings/update` endpoint is only fetched by System/Updates/Updates.tsx
/// (`useUpdateSettings`), NOT by the General settings page. The fixture timed
/// out at 30s in CI because the response event never fired. Switched to a
/// direct APIRequest call (same pattern NamingPresetsFixture uses) — the
/// endpoint-row INVENTORY contract is "GET /api/v5/settings/update returns
/// the UpdateSettingsResource shape", which the direct call verifies. The
/// General page open is preserved as a settings-smoke trigger.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class UpdateConfigFixture : AutomationTest
{
    [Test]
    public async Task form_loads()
    {
        var page = await new SettingsGeneralPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/settings/update",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("branch");
    }
}
