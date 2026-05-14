using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-15 update — InteractiveSearchGrabFixture (D-14 PRSmoke).
//
// Asserts the full Grab → History chained-system pipeline. Plan 18-08 shipped
// this fixture with a local SeedMangaAsync helper; Plan 18-15 replaces the
// helper with the canonical AddMangaFlow.AddByMangaDexIdAsync call.
//
// The fixture remains [Explicit] because Plan 18-14 D-D
// (AddMangaModal.ConfirmAddAsync nav race, GitHub issue #102) blocks every
// fixture that goes through AddMangaFlow. Flips automatically when D-D ships.
//
// This is THE state-not-rendering regression catcher per memory
// feedback_verify_ui_state_not_just_rendering.md: a grab that 404s, returns
// silently, or fails to write history would leave the row visible AND the
// no-results placeholder absent — a count-based assertion misses both. The
// chained-system history-row assertion proves the pipeline end-to-end.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
[Explicit("Phase 19 Cat B (Residual Yellow Inventory Resolution): needs indexer-side cassettes - the grab path hits indexers, not the /manga/{id} metadata endpoint the existing cassette set covers. #102 is CLOSED and was NOT the blocker. Flip when Phase 19 extends the cassette dir per DEF-18-19-01. See ROADMAP Phase 19 SC#2.")]
public class InteractiveSearchGrabFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task grab_writes_to_history()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Derive slug from the post-Add URL.
        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        await SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync(Page, RootUri, slug);

        // STATE assertion (chained-system): a history row materializes after
        // the grab. The `manga-history-row-{id}` testid is Plan-05's
        // deliverable; the regex pattern matches the runtime emission.
        await Page.GotoAsync($"{RootUri}/manga/activity/history");

        var historyRows = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));

        // SignalR push may take a few seconds; allow up to 30s for the row.
        await Assertions.Expect(historyRows.First).ToBeVisibleAsync(new()
        {
            Timeout = 30_000
        });
        var count = await historyRows.CountAsync();
        count.Should().BeGreaterThan(0, "grab must produce at least one history row");
    }
}
