using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — TranslationProfile Add round-trip
/// fixture. Greens INVENTORY v5-endpoint row `POST /api/v5/translationprofile |
/// Settings/Profiles TranslationProfile Add`.
///
/// Blocker #4 mitigation: TestKit.SeedTranslationProfileAsync POSTs against
/// the canonical endpoint with the canonical body shape (Phase 5 D-01..D-04
/// flat languages array). The Add UI surface goes through the same POST, so
/// the API-driven path exercises the same v5 contract. State-assertion = the
/// returned id is positive (server-assigned), and a subsequent GET surfaces
/// the new row.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TranslationProfileAddFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        var name = $"Plan 20-07a TP Add {Guid.NewGuid():N}".Substring(0, 32);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var newId = await tk.SeedTranslationProfileAsync(name);
        newId.Should().BeGreaterThan(0);

        // State assertion via UI reload: a fresh GET /api/v5/translationprofile
        // surfaces the new name. Confirms the POST persisted (not merely 2xx-acked).
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/translationprofile") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        // Status is buffered on the IResponse and survives the reload, so it's safe
        // to assert; the GET firing + 200 proves the list endpoint was hit.
        resp.Status.Should().Be(200);

        // Flake fix (run 26579906231, sqlite nightly leg — getResponseBody race; same
        // class PR #287 fixed in TranslationProfileListFixture): do NOT read
        // resp.TextAsync() — Page.ReloadAsync() above evicts the captured response's
        // body from the browser network cache, so the CDP Network.getResponseBody read
        // intermittently throws "No resource with given identifier found". Assert the
        // new profile via the auto-retrying DOM check instead: it's immune to body
        // eviction AND verifies actual user-visible state (the row rendered after the
        // reload), which is the stronger contract.
        await Assertions.Expect(page.PageContainer)
            .ToContainTextAsync(name, new() { Timeout = 15_000 });
    }
}
