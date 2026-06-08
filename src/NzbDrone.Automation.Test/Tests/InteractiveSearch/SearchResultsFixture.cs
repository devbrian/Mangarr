using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch sweep) — v5-endpoint axis
/// `GET /api/v5/manga/release` (INVENTORY row 101: InteractiveSearch results).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy) maps to PRSmoke
/// per the mechanical row-axis rule.
///
/// Drives the canonical InteractiveSearch open flow (AddMangaFlow seed →
/// MangaDetails Search tab → wait for settled state) and asserts that
/// GET /api/v5/manga/release fires and returns 200.
///
/// Phase 39 (Plan 39-07 gap-closure) empty-feed contract: the in-process
/// MangaDex/Comix site-scraper indexers that produced release rows were retired
/// in Plan 39-03. The sole surviving IIndexer is the GatewayIndexer, which is
/// seeded DISABLED-by-default, so the InteractiveSearch fan-out now yields ZERO
/// release rows. This fixture therefore proves two things:
///   1. GET /api/v5/manga/release still returns 200 on the Search tab navigation
///      (the endpoint is wired + the search fan-out completes without error even
///      with no enabled indexer).
///   2. The feed is now EMPTY (zero release rows) — the post-retirement state.
/// A non-empty feed is the future gateway-indexer harness fixture's job (an
/// enabled-GatewayIndexer interactive-search fixture, owned by the Phase-37
/// gateway work) — NOT this fixture's contract.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SearchResultsFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task results_render()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

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

        // STATE assertion 1: GET /api/v5/manga/release returned 200. The endpoint
        // is wired and the search fan-out completes without error even though no
        // enabled indexer exists post-retirement.
        var resp = await releaseTask;
        resp.Status.Should().Be(
            200,
            "GET /api/v5/manga/release must return 200 on the InteractiveSearch tab open flow");

        // STATE assertion 2 (Phase 39 empty-feed contract): the feed renders ZERO
        // release rows. The sole indexer is the disabled-by-default GatewayIndexer;
        // the in-process MangaDex/Comix feed was retired in Phase 39 (Plan 39-03), so
        // the search fan-out has no enabled source and yields no rows. A non-empty
        // feed is the gateway-indexer harness fixture's job, a future addition.
        var count = await modal.GetReleaseCountAsync();
        count.Should().Be(
            0,
            "sole indexer is the disabled-by-default GatewayIndexer; the in-process MangaDex/Comix feed was retired in Phase 39 (Plan 39-03) — a non-empty feed is the gateway-indexer harness fixture's job, a future addition");
    }
}
