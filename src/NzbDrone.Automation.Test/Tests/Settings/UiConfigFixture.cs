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

        var configTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/settings/ui") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await configTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("theme");
    }
}
