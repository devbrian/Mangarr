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
// feed cassette (see recording-loop header below) — but the fixture stays
// [Explicit]. Plan 19-02's recording session uncovered a SEPARATE, larger
// blocker that is NOT a cassette gap: with Comix disabled, the MangaDex-only
// InteractiveSearch for the seeded manga fetches releases (the backend logs
// `MangaDownloadDecisionMaker: Processing 3 manga releases`) but the modal
// renders ZERO release rows — so modal.GetReleaseCountAsync() returns 0 and
// the grab path is never reached. The 3 indexer-feed chapters (Komi 288,
// 500, 500.5) do not map onto the Chapter rows the AddManga *metadata* feed
// synced, so the DecisionEngine produces nothing the React modal renders.
// This is an InteractiveSearch decision/render defect tracked as a Phase 19
// follow-up issue — flip [Explicit] when that defect is fixed.
//
// ── Cassette recording loop (precedent: AddMangaSearchFixture.cs:14-16) ──
// Recorded 2026-05-14 (Plan 19-02) via:
//   MANGARR_TEST_CASSETTE_MODE=Record
//   MANGARR_TEST_CASSETTE_DIR=src/NzbDrone.Automation.Test/Fixtures/Cassettes/MangaDex
//   MANGARR_TEST_ASSEMBLY_PATH=_tests/net10.0/Mangarr.Automation.Test.dll
// The fixture drove the real open → search flow against api.mangadex.org;
// the InteractiveSearch indexer feed GET
// (GET /manga/{id}/feed?...&includes[]=manga&translatedLanguage[]=en) was
// persisted as a SHA1-keyed {key}.json cassette. The grab path's
// GET /at-home/server/{chapterId} could NOT be recorded because the modal
// renders 0 rows (see above) so GrabAsync is never reached. Per-page image
// GETs to uploads.mangadex.org are sentinel-PNG'd by
// CassetteHandler.IsImageContentType and NOT persisted. CI runs Replay mode.
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 19 Cat B (DEF-18-19-01): Plan 19-02 wired Comix-disable + recorded the InteractiveSearch indexer-feed cassette, but uncovered a separate non-cassette blocker — MangaDex-only InteractiveSearch fetches 3 releases yet the modal renders 0 rows (DecisionEngine/render path), so the grab is never reached. Flip when the InteractiveSearch 0-render defect is fixed (Phase 19 follow-up issue).")]
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
