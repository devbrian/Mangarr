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
// This is THE state-not-rendering regression catcher per memory
// feedback_verify_ui_state_not_just_rendering.md: a grab that 404s, returns
// silently, or fails to write history would leave the row visible AND the
// no-results placeholder absent — a count-based assertion misses both. The
// chained-system history-row assertion proves the pipeline end-to-end.
//
// Phase 19 Cat B (DEF-18-19-01): Plan 19-02 wired the Comix-disable
// OneTimeSetUp (Phase 19 D-05) and recorded the InteractiveSearch indexer
// feed cassette. The recording session uncovered DEF-19-02-01 (the
// InteractiveSearch 0-render defect), which the Phase 19 in-phase debug
// session then RESOLVED — see .planning/debug/resolved/
// interactive-search-0-rows-def-19-02-01.md. With the DecisionEngine
// manga-resolution / CustomFormatProfile-degradation fixes, the empty
// customFormats list, the OpenForMangaAsync settle-wait, and the GrabAsync
// POST-response wait, the full open → search → grab → history pipeline runs
// offline — so [Explicit] is removed and this fixture rejoins the suite.
// [Category("PRSmoke")] is PRESERVED — this is a core top-nav user path.
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
[Category("PRSmoke")]
public class InteractiveSearchGrabFixture : AutomationTest
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
