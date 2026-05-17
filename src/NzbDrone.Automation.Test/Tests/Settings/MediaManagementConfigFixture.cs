using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — MediaManagement settings form-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/settings/mediamanagement | Settings/MediaManagement form load`.
///
/// Migrated 2026-05-17 (Phase 22 PR #197 CI fix) from
/// <c>WaitForResponseAsync + ReloadAsync</c> to direct <c>Page.APIRequest.GetAsync</c>
/// per Phase 20 LEARNINGS SL-2 — the prior pattern raced on Playwright's
/// <c>Network.getResponseBody</c> "No resource with given identifier found"
/// protocol error when the reload triggered DOM teardown faster than the
/// captured Response object's body could be read.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MediaManagementConfigFixture : AutomationTest
{
    [Test]
    public async Task form_loads()
    {
        var page = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/settings/mediamanagement",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("recycleBin");
    }
}
