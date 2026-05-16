using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — TranslationProfile list-load fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/translationprofile |
/// Settings/Profiles TranslationProfile list`.
///
/// Blocker #4 mitigation: seeds a TranslationProfile via TestKit so the list
/// is guaranteed non-empty (replaces the "no row present" Inconclusive branch
/// the plan-spec anticipated).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TranslationProfileListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a TP List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedTranslationProfileAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/translationprofile") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
