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

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task lookup_returns_results()
    {
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
}
