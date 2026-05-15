using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — CustomFormat list-load fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/customformat |
/// Settings/CustomFormats list`.
///
/// Blocker #4 mitigation: seeds a CustomFormat in OneTimeSetUp via
/// <see cref="TestKit.TestKit.SeedCustomFormatAsync"/> so the list response
/// is guaranteed non-empty (no Inconclusive branch).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CustomFormatListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a CF List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/customformat") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
