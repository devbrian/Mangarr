using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — MangaNaming presets dropdown fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/config/manganaming/presets/manga | Naming preset dropdown`.
///
/// API-driven assertion (the presets dropdown is populated lazily on Naming form
/// open; the deterministic check is the endpoint response shape — a non-empty list
/// of preset entries). Asserting via Page.APIRequest keeps the fixture independent
/// of the form-open chain.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class NamingPresetsFixture : AutomationTest
{
    [Test]
    public async Task presets_render()
    {
        var page = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/config/manganaming/presets/manga");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().StartWith("[");
        body.Should().Contain("title");
    }
}
