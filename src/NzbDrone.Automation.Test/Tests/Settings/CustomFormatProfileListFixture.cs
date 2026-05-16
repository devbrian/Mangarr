using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — CustomFormatProfile list-load fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/customformatprofile |
/// Settings/CustomFormatProfiles list`.
///
/// Blocker #4 mitigation: seeds a CustomFormatProfile in OneTimeSetUp via
/// <see cref="TestKit.TestKit.SeedCustomFormatProfileAsync"/>.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CustomFormatProfileListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a CFP List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatProfileAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsCustomFormatProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/customformatprofile") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
