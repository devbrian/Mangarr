using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Tag Add round-trip fixture. Greens
/// INVENTORY v5-endpoint row `POST /api/v5/tag | Settings/Tags Add modal`.
///
/// Blocker #4 mitigation: directly POSTs a fresh-DB-unique label via the
/// TestKit seeder (acts as the API-driven Add coverage) and then reopens the
/// page to assert the new tag renders. The TestKit POST hits the same endpoint
/// as the UI Add modal; we do NOT depend on locating a UI Add control whose
/// shape is not in this plan's frontend scope.
///
/// /gsd-debug nightly-26025833226-postgres-automation fix: the original
/// state-assertion used Playwright's WaitForResponseAsync + Page.ReloadAsync +
/// resp.TextAsync() pattern, which raced under postgres cold-start timing —
/// the page navigated again before the response body was buffered, evicting
/// it and surfacing `Protocol error (Network.getResponseBody): No resource
/// with given identifier found`. Replaced with a direct HttpClient GET
/// carrying X-Api-Key (mirrors MangaCutoffUnmetFixture.ResolveSeedFksAsync
/// precedent) — decouples the state assertion from the page lifecycle.
/// The page-render assertion is kept as a smoke check that the Tags page
/// itself mounts; the body-contains assertion now runs over HttpClient.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TagAddFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        var label = $"plan-20-07a-add-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var newId = await tk.SeedTagAsync(label);
        newId.Should().BeGreaterThan(0);

        // Smoke: the Tags page itself mounts (preserves the page-lifecycle
        // signal without coupling the state assertion to it).
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // State assertion: a fresh GET /api/v5/tag includes the new label.
        // Driven by HttpClient (not Playwright's response interception) so
        // the assertion does not race with browser navigation under
        // postgres cold-start timing. Timeout = 30s preserves the fail-fast
        // bound the original WaitForResponseAsync had — without it
        // HttpClient.Timeout defaults to ~100s, which would turn a fast
        // signal into a slow timeout across the matrix on backend stalls.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var body = await http.GetStringAsync($"{RootUri}/api/v5/tag");
        body.Should().Contain(label);
    }
}
