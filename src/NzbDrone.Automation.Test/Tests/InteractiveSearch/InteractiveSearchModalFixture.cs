using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-15 gap closure — modal-action InteractiveSearchModal
// (INVENTORY line 166). "MangaDetails / Wanted Manual Search".
//
// Canonical end-to-end InteractiveSearch fixture using the BL-03-fixed UUID-
// aware row regex from Plan 18-13. Seeds a manga, opens the Search tab,
// counts release rows, grabs the first release, and asserts a history row
// materializes — the full open → search → grab → history chained pipeline.
//
// State assertion (per feedback_verify_ui_state_not_just_rendering): row
// count > 0 (BL-03 fix validates the regex catches UUID-shaped row testids);
// post-grab, the manga-history-row-{id} testid materializes within 30s
// (SignalR push + history rendering proven together).
//
// [Explicit] cite: blocked by AddMangaFlow D-D nav race (issue #102).
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 19 Cat B (Residual Yellow Inventory Resolution): needs indexer-side cassettes - the grab path hits indexers, not the /manga/{id} metadata endpoint the existing cassette set covers. #102 is CLOSED and was NOT the blocker. Flip when Phase 19 extends the cassette dir per DEF-18-19-01. See ROADMAP Phase 19 SC#2.")]
public class InteractiveSearchModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task open_search_grab()
    {
        var detailsPage = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // After AddMangaFlow lands on /manga/{slug}, derive the slug from the
        // Page URL so we can pass it to OpenForMangaAsync (the modal helper
        // navigates explicitly to /manga/{slug} + flips to the Search tab).
        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);

        // STATE assertion 1: BL-03 regex fix validation — release rows
        // materialize. A zero-count here would prove the BL-03 negative-
        // lookahead regex is still broken against UUID-shaped row testids.
        var count = await modal.GetReleaseCountAsync();
        count.Should().BeGreaterThan(
            0,
            "the BL-03 UUID-aware row regex must catch at least one release row");

        // Grab the first release. Triggers POST /api/v5/queue/grab/{id} via
        // InteractiveSearchRow.tsx's grab-button handler.
        await modal.GrabAsync(0);

        // STATE assertion 2 (chained-system): a history row materializes
        // after the grab. The manga-history-row-{id} testid is Plan-05's
        // deliverable; the regex matches the runtime emission.
        await Page.GotoAsync($"{RootUri}/manga/activity/history");

        var historyRows = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));

        // SignalR push may take a few seconds; allow up to 30s.
        await Assertions.Expect(historyRows.First).ToBeVisibleAsync(new()
        {
            Timeout = 30_000
        });

        var historyCount = await historyRows.CountAsync();
        historyCount.Should().BeGreaterThan(
            0,
            "the grab must produce at least one history row via the SignalR push pipeline");
    }
}
