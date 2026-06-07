using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 AddManga modal sweep) — v5-endpoint axis
/// `GET /api/v5/manga/lookup` (INVENTORY row 76).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy) maps to PRSmoke
/// per the mechanical row-axis rule.
///
/// Drives the AddManga search input flow and asserts the lookup endpoint
/// fires + returns a non-empty results array (cassette-replayed MangaDex).
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manga/lookup returns 200.
///   2. Response body is a non-empty JSON array (the lookup results).
///   3. At least one search-result row renders in the UI.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaLookupFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task lookup_returns_results()
    {
        // Phase 41 (41-03 D-01a) flipped the DEFAULT primary metadata source from
        // MangaDex to MangaBaka. This fixture pastes a MangaDex UUID and asserts
        // cassette-replayed MangaDex results, and the lookup endpoint dispatches to
        // IMetadataSourceFactory.GetPrimary() at request time — so MangaDex is no
        // longer the default and must be promoted to primary explicitly first.
        await EnsureMangaDexPrimaryAsync();

        var addPage = await new AddMangaPage(Page).OpenAsync(RootUri);

        // Arm the lookup response listener BEFORE typing into the search input
        // so the in-flight GET cannot be missed.
        var lookupTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/lookup") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        // Filling triggers the AddNewManga.tsx debounced lookup (500ms).
        await addPage.SearchInput.FillAsync(KnownMangaDexId);

        // STATE assertion 1: GET /api/v5/manga/lookup returned 200.
        var resp = await lookupTask;
        resp.Status.Should().Be(
            200,
            "GET /api/v5/manga/lookup must return 200 for the AddManga search input flow");

        // STATE assertion 2: response body is a non-empty JSON array.
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "lookup response must include the MangaResource search payload");
        body.Should().StartWith(
            "[",
            "lookup response must be a JSON array of MangaResource (the lookup-results envelope)");
        body.Should().NotBe(
            "[]",
            "cassette-replayed MangaDex must return at least one result for the known UUID");

        // STATE assertion 3: at least one search-result row renders in the UI.
        var resultRow = addPage.ResultRowByKey(KnownMangaDexId);
        await resultRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
        var count = await Page.Locator("[data-testid^='add-manga-result-']").CountAsync();
        count.Should().BeGreaterThan(
            0,
            "AddNewMangaSearchResult rows must render after the lookup returns results");
    }

    /// <summary>
    /// Seed a MangaDex metadata source and promote it to primary. Required since Phase 41
    /// (41-03 D-01a) made MangaBaka the default primary: a fresh DB auto-seeds ONLY the
    /// primary-default provider, so no MangaDex row exists out-of-the-box. This fixture
    /// pastes a MangaDex UUID and asserts cassette-replayed MangaDex results, and the lookup
    /// endpoint dispatches to <c>GetPrimary()</c> at request time — so MangaDex must be both
    /// present and primary first. Mirrors <c>MetadataSourceSetPrimaryFixture</c>.
    /// </summary>
    private async Task EnsureMangaDexPrimaryAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var mangaDexId = await tk.SeedMetadataSourceAsync("MangaDex (lookup primary)");

        var setPrimaryResp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/metadatasource/{mangaDexId}/setprimary",
            new APIRequestContextOptions { DataObject = new { } });
        setPrimaryResp.Status.Should().BeInRange(
            200,
            299,
            "MangaDex must be promoted to primary so the UUID lookup routes to the MangaDex cassette");
    }
}
