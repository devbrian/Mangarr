using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch sweep) — v5-endpoint axis
/// `GET /api/v5/manga/release` (INVENTORY row 99: InteractiveSearch results).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy) maps to PRSmoke
/// per the mechanical row-axis rule.
///
/// Drives the canonical InteractiveSearch open flow (AddMangaFlow seed →
/// MangaDetails Search tab → wait for results) and asserts that
/// GET /api/v5/manga/release fires and returns the release rows.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manga/release returns 200 on the Search tab navigation.
///   2. The InteractiveSearch table renders at least one release row.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SearchResultsFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task results_render()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        // Arm the response listener BEFORE opening the Search tab. The
        // InteractiveSearchModal OpenForMangaAsync helper navigates to
        // /manga/{slug} + clicks the Search tab, which fires GET
        // /api/v5/manga/release via the `useReleases` hook.
        var releaseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/release") && r.Request.Method == "GET",
            new() { Timeout = 60_000 });

        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);

        // STATE assertion 1: GET /api/v5/manga/release returned 200.
        var resp = await releaseTask;
        resp.Status.Should().Be(
            200,
            "GET /api/v5/manga/release must return 200 on the InteractiveSearch tab open flow");

        // STATE assertion 2: the modal renders >= 1 release row (BL-03 UUID-
        // aware regex). A zero-count here would prove either the cassette is
        // not replaying or the BL-03 regex regressed.
        var count = await modal.GetReleaseCountAsync();
        count.Should().BeGreaterThan(
            0,
            "InteractiveSearch must render at least one release row from the cassette-replayed indexer feed");
    }
}
