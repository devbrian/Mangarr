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

        // Status is buffered on the IResponse and survives the reload.
        resp.Status.Should().Be(200);

        // Flake-proofing (getResponseBody race, same class as PR #287 / run 26579906231,
        // sqlite nightly leg): do NOT read resp.TextAsync() — Page.ReloadAsync() above evicts
        // the captured response's body from the browser network cache, so the CDP
        // Network.getResponseBody read intermittently throws "No resource with given identifier
        // found". Assert the seeded profile via the auto-retrying DOM check instead: immune to
        // body eviction AND verifies actual user-visible state (the row rendered after reload).
        await Assertions.Expect(page.PageContainer)
            .ToContainTextAsync(SeedName, new() { Timeout = 15_000 });
    }
}
