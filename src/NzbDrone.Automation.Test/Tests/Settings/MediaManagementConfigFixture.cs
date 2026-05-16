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

        var configTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/settings/mediamanagement") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await configTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("recycleBin");
    }
}
