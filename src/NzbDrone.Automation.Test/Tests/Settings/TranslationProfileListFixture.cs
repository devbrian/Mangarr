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

        // /gsd-debug nightly-ci-failures Failure B fix: this fixture used to read the
        // GET /api/v5/translationprofile network response body via resp.TextAsync().
        // That P/Invokes Chromium's Network.getResponseBody against the response we
        // matched BEFORE the reload — but after Page.ReloadAsync() the browser may have
        // already evicted that response's body (the resource is no longer retained),
        // throwing "Protocol error (Network.getResponseBody): No resource with given
        // identifier found". The sqlite nightly leg flaked on exactly this race while the
        // postgres legs passed, confirming non-determinism rather than a backend bug.
        //
        // We still wait for the GET to fire (proves the endpoint is hit + 200s on reload),
        // but assert the user-visible RESULT against the rendered DOM instead of the
        // network body. Playwright's auto-retrying Expect(...).ToContainTextAsync polls the
        // live page, so it is immune to the body-eviction race and verifies actual UI state
        // (the seeded profile row renders) rather than a transient network artifact.
        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/translationprofile") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);

        // Re-wait for the page container after the reload, then assert the seeded profile
        // is rendered. ToContainTextAsync retries up to the default expect timeout, so it
        // tolerates the list hydrating asynchronously after the GET completes.
        await page.WaitForLoadedAsync();
        await Assertions.Expect(page.PageContainer).ToContainTextAsync(SeedName);
    }
}
