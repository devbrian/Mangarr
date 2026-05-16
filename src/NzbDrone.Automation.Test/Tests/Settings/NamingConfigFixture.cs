using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — MangaNaming config form-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/config/manganaming | Naming form load`.
///
/// MangaNaming lives on /settings/mediamanagement (Phase 15 Plan 15-12 fix-forward —
/// the TV Naming sub-route was deleted). The form is rendered by MangaNaming.tsx
/// which calls GET /api/v5/config/manganaming on mount.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class NamingConfigFixture : AutomationTest
{
    [Test]
    public async Task form_loads()
    {
        var page = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var configTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/config/manganaming") && !r.Url.Contains("presets") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await configTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("standardChapterFormat");
    }
}
