using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-15 update — InteractiveSearchGrabFixture (D-14 PRSmoke).
//
// Asserts the full Grab → History chained-system pipeline. Plan 18-08 shipped
// this fixture with a local SeedMangaAsync helper; Plan 18-15 replaces the
// helper with the canonical AddMangaFlow.AddByMangaDexIdAsync call.
//
// This is THE state-not-rendering regression catcher per memory
// feedback_verify_ui_state_not_just_rendering.md: a grab that 404s, returns
// silently, or fails to write history would leave the row visible AND the
// no-results placeholder absent — a count-based assertion misses both. The
// chained-system history-row assertion proves the pipeline end-to-end.
//
// Phase 33 (COMIX2-01): the Phase 19 D-05 Comix-disable OneTimeSetUp is LIFTED.
// Comix is exercised offline via CassettingComixSigner (Plan 33-02) reading
// recorded cassettes under Fixtures/Cassettes/Comix/ (Plan 33-03). The modal is
// opened inline so the mixed-source assertion (≥1 data-source='Comix' row) runs
// before the grab. Closes GH #116 (atomic in Plan 33-06).
// The PRSmoke category is PRESERVED — this is a core top-nav user path.
//
// ── Cassette recording loop (precedent: AddMangaSearchFixture.cs:14-16) ──
// Indexer-feed cassette recorded 2026-05-14 (Plan 19-02); the grab-path
// GET /at-home/server/{chapterId} cassette (77cba1bcd9755d35.json) recorded
// 2026-05-14 (DEF-19-02-01 debug session, ReplayOrRecord mode) once the
// 0-render defect fix made the grab reachable. Both recorded via:
//   MANGARR_TEST_CASSETTE_MODE=Record|ReplayOrRecord
//   MANGARR_TEST_CASSETTE_DIR=src/NzbDrone.Automation.Test/Fixtures/Cassettes
//   MANGARR_TEST_ASSEMBLY_PATH=_tests/net10.0/Mangarr.Automation.Test.dll
// driving the real open → search → grab flow against api.mangadex.org.
// Per-page image GETs to uploads.mangadex.org are sentinel-PNG'd by
// CassetteHandler.IsImageContentType and NOT persisted. CI runs Replay mode.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class InteractiveSearchGrabFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    // Phase 33 COMIX2-01: Comix exercised offline via CassettingComixSigner +
    // recorded cassettes under Fixtures/Cassettes/Comix/. GH #268 inverted the
    // automation baseline to disable Comix by default, so this fixture opts out to
    // keep the seeded Comix indexer ENABLED for its offline cassette-replayed fan-out.
    protected override bool DisableComixIndexerInBaseline => false;

    [Test]
    public async Task grab_writes_to_history()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Derive slug from the post-Add URL.
        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        // Open the modal inline (instead of SearchAndGrabFlow) so the mixed-source
        // assertion can run against ModalRoot before the grab. data-source='Comix'
        // is Plan 33-01's attribute, proving the cassetting signer drives a real
        // Comix fan-out offline.
        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);

        var comixCount = await modal.ModalRoot.Locator("[data-source='Comix']").CountAsync();
        comixCount.Should().BeGreaterThan(
            0,
            "Phase 33 D-11: Comix indexer must render at least one row via cassetting signer + recorded cassette");

        await modal.GrabAsync(0);

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
