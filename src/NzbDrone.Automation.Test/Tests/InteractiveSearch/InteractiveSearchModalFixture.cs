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
// Phase 33 (COMIX2-01): the Phase 19 D-05 Comix-disable OneTimeSetUp is LIFTED.
// Comix is now exercised offline via CassettingComixSigner (Plan 33-02) reading
// recorded cassettes under Fixtures/Cassettes/Comix/ (Plan 33-03 LIVE recording).
// The fan-out hits MangaDex AND Comix; the mixed-source assertion below proves at
// least one data-source='Comix' row renders end-to-end (signer → indexer →
// DecisionEngine → API → React). Closes GH #116 (atomic in Plan 33-06).
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
public class InteractiveSearchModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    // Phase 33 COMIX2-01: Comix exercised offline via CassettingComixSigner +
    // recorded cassettes under Fixtures/Cassettes/Comix/. GH #268 inverted the
    // automation baseline to disable Comix by default, so this fixture opts out to
    // keep the seeded Comix indexer ENABLED for its offline cassette-replayed fan-out.
    protected override bool DisableComixIndexerInBaseline => false;

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

        // STATE assertion (mixed-source): at least one row is sourced from Comix,
        // proving the cassetting signer + recorded cassette drive a real Comix
        // fan-out through the DecisionEngine → API → React render. data-source is
        // Plan 33-01's InteractiveSearchRow.tsx attribute (= IndexerDefinition.Name).
        var comixRows = modal.ModalRoot.Locator("[data-source='Comix']");
        var comixCount = await comixRows.CountAsync();
        comixCount.Should().BeGreaterThan(
            0,
            "Phase 33 D-11: Comix indexer must render at least one row via cassetting signer + recorded cassette");

        // Grab the first release. Triggers POST /api/v5/manga/queue/grab/{id}
        // via InteractiveSearchRow.tsx's grab-button handler.
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
