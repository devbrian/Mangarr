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
// Phase 19 Cat B (DEF-18-19-01): Plan 19-02 wired the Comix-disable
// OneTimeSetUp (Phase 19 D-05) and recorded the InteractiveSearch indexer
// feed cassette. The recording session uncovered DEF-19-02-01 (the
// InteractiveSearch 0-render defect), which the Phase 19 in-phase debug
// session then RESOLVED — see .planning/debug/resolved/
// interactive-search-0-rows-def-19-02-01.md. That fix:
//   * MangaDownloadDecisionMaker now resolves the searched manga from
//     MangaSearchCriteria.Manga instead of fuzzy-title-matching the
//     romanized release title (which never matched the English-altTitle
//     Manga.Title), and degrades gracefully on a zero / orphaned
//     CustomFormatProfile id instead of throwing every release into a
//     DecisionError rejection;
//   * MangaReleaseResource emits an empty customFormats list, never null;
//   * InteractiveSearchModal.OpenForMangaAsync now waits for the SETTLED
//     steady state (table OR no-results) and GrabAsync awaits the grab POST
//     response before returning.
// With those fixes the modal renders the 3 indexer-feed rows, the grab
// completes, and a history row materializes — so [Explicit] is removed and
// this fixture runs in the offline Replay suite.
//
// ── Cassette recording loop (precedent: AddMangaSearchFixture.cs:14-16) ──
// Indexer-feed cassette recorded 2026-05-14 (Plan 19-02); the grab-path
// GET /at-home/server/{chapterId} cassette (77cba1bcd9755d35.json) recorded
// 2026-05-14 (DEF-19-02-01 debug session, ReplayOrRecord mode) once the
// 0-render defect fix made the grab reachable. Both recorded via:
//   MANGARR_TEST_CASSETTE_MODE=Record|ReplayOrRecord
//   MANGARR_TEST_CASSETTE_DIR=src/NzbDrone.Automation.Test/Fixtures/Cassettes/MangaDex
//   MANGARR_TEST_ASSEMBLY_PATH=_tests/net10.0/Mangarr.Automation.Test.dll
// driving the real open → search → grab flow against api.mangadex.org.
// Per-page image GETs to uploads.mangadex.org are sentinel-PNG'd by
// CassetteHandler.IsImageContentType and NOT persisted. CI runs Replay mode.
[TestFixture]
[Category("AutomationTest")]
public class InteractiveSearchModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Phase 19 D-05: Comix cannot be HTTP-cassette'd (Phase 18 D-11 — the
        // runtime signer hits comix.to live). Disable it BEFORE the first
        // InteractiveSearch so the fan-out is MangaDex-only. NUnit runs the
        // base AutomationTest [OneTimeSetUp] (which boots the backend + seeds
        // the baseline) before this derived [OneTimeSetUp], so RootUri/ApiKey
        // are wired by the time this runs.
        //
        // FOLLOW-UP: https://github.com/devbrian/Mangarr/issues/116 — restore
        // Comix search/grab coverage here once a Comix offline-tier cassette
        // mechanism exists OR the IComixIndexer boundary-mock pattern
        // (Phase 18 D-11) is applied to this fixture.
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

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
