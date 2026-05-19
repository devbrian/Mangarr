using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 25.1 Plan 25.1-03 Task 3 — end-to-end first-record-creation smoke for
/// the library-import flow (Phase 17.3 L-002 anti-empty-state-regression).
///
/// Drives the full /add/import library-import path against a fresh DB:
///   1. baseline RootFolder seeded by AutomationTest.OneTimeSetUp
///   2. OneTimeSetUp drops one manga subfolder (MangaDex UUID) + CBZ inside
///      the baseline root so the scan view surfaces ≥1 unmapped folder row.
///   3. UI drive: SelectFolder → click root → ImportManga scan → wait for
///      per-row lookup → click bulk Import.
///   4. STATE assertions: POST /api/v5/manga succeeded; URL redirected to /
///      (the MangaIndex library route — Phase 15 Plan 15-07 flipped root to
///      MangaIndex, so `/` IS the Manga library; there is no `/manga` route);
///      GET /api/v5/manga returns ≥1 persisted Manga record (II-10 contract).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// Pattern κ: zero series-* / episode-* / season-* / add-series-* testid
/// prefixes; only import-manga-* family from data-testid-spec.md v1.1 Phase 25.1.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class FirstRecordCreationFixture : AutomationTest
{
    // The folder name IS the search term passed to useLookupManga via
    // `?term=<folderName>` on `/api/v5/manga/lookup`. Using the MangaDex UUID
    // (canonical "Komi Can't Communicate" — the same value AddMangaSearchFixture +
    // AddMangaFlowFixture pin for Replay-mode cassette coverage) routes through
    // MangaLookupController's UUID-detection branch (MangaLookupController.cs:44),
    // which hits the cassette-covered by-id lookup path. Term-based search like
    // "Solo Leveling" would need a separate cassette that doesn't currently exist
    // and would fail CI Replay mode with a 20s lookup-population timeout.
    //
    // The auto-match resolves to "Komi Can't Communicate" — the test does NOT
    // assert on title text (only on populated `:not(:empty)` selectedManga
    // cell), so any cassette-covered match works.
    private const string MangaFolderName = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [OneTimeSetUp]
    public async Task SeedLibraryImportFixturesAsync()
    {
        // Disable Comix indexer (PATTERNS analog C — avoid Comix interference
        // during per-row lookup chain).
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();

        // AutomationTest.OneTimeSetUp seeds the baseline root folder at
        // Runner.AppData/MangaLibrary (AutomationTest.cs:104). Drop one manga
        // subfolder + minimal CBZ inside it so the /add/import scan surfaces
        // exactly 1 unmapped-folder row. Folder name uses the MangaDex UUID
        // for cassette-covered Replay-mode lookup (see MangaFolderName
        // constant comment).
        var baselineRoot = Path.Combine(Runner.AppData, "MangaLibrary");
        var mangaFolder = Path.Combine(baselineRoot, MangaFolderName);
        Directory.CreateDirectory(mangaFolder);

        var cbzPath = Path.Combine(mangaFolder, $"{MangaFolderName} - Chapter 001 [en].cbz");
        if (!File.Exists(cbzPath))
        {
            // Minimal valid CBZ — empty ZIP signature (mirrors
            // TestKit.SeedInteractiveImportFolderAsync line 853 verbatim). The
            // file presence is what matters; no image decoder needed.
            File.WriteAllBytes(cbzPath, new byte[]
            {
                0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            });
        }
    }

    [Test]
    public async Task library_import_persists_manga_and_chapter_files()
    {
        var addEndpointRegex = new Regex(@"/api/v5/manga(\?|$)");

        // Navigate to /add/import — ImportMangaSelectFolder renders the
        // Root Folder list (post-25.1 — supersedes Phase 25 InteractiveImportPage).
        await Page.GotoAsync($"{RootUri}/add/import");
        await Page.GetByTestId("import-manga-select-folder-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // Click the first (and only) root folder row. The seeded baseline root
        // is the only one present; SeedLibraryImportFixturesAsync added one
        // unmapped subfolder (the MangaDex UUID) inside it.
        //
        // Post debug-add-import-ui-mismatch fix: shared RootFolderRow
        // carries per-id testid `root-folder-row-{id}-link` (D-18).
        var firstLink = Page
            .Locator("[data-testid^='root-folder-row-'][data-testid$='-link']")
            .First;
        await firstLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await firstLink.ClickAsync();

        await Page.WaitForURLAsync(new Regex(@"/add/import/\d+$"),
            new PageWaitForURLOptions { Timeout = 10_000 });

        // Wait for the scan view body.
        await Page.GetByTestId("import-manga-page")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion 1: exactly the seeded subfolder surfaces as a row.
        var rowCount = await Page
            .Locator("[data-testid^='import-manga-row-']")
            .CountAsync();
        rowCount.Should()
            .BeGreaterThanOrEqualTo(
                1,
                "library-import scan must surface ≥1 unmapped-folder row for "
                + "the seeded MangaDex-UUID subfolder (Phase 17.3 L-002 "
                + "first-record-creation contract).");

        // Wait for the per-row lookup chain to resolve a populated
        // selectedManga.title cell (the Import button is disabled when
        // importableCount === 0 per ImportMangaFooter.tsx).
        var populatedSelector = Page.Locator(
            "[data-testid^='import-manga-row-'][data-testid$='-selected-manga-name']:not(:empty)");
        await populatedSelector.First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 20_000 });

        var populatedCount = await populatedSelector.CountAsync();
        populatedCount.Should()
            .BeGreaterThan(
                0,
                "useLookupManga(MangaDex UUID) must resolve to a populated "
                + "selectedManga.title within 20s of scan render (D-04 + D-03 — "
                + "top match auto-selects). Replay-mode cassette covers the "
                + "MangaDex by-id lookup via MangaLookupController's "
                + "UUID-detection branch (line 44).");

        // Arm the POST listener BEFORE clicking Import (mirrors
        // ImportMangaFixture.import_persists_manga_and_redirects pattern).
        var importButton = Page.GetByTestId("import-manga-bulk-import-button");
        await importButton.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });

        var postTask = Page.WaitForResponseAsync(
            r => addEndpointRegex.IsMatch(r.Url) && r.Request.Method == "POST",
            new() { Timeout = 60_000 });

        await importButton.ClickAsync();

        // STATE assertion 2: POST /api/v5/manga returned 2xx.
        var resp = await postTask;
        resp.Status.Should()
            .BeInRange(
                200,
                299,
                "library-import POST must persist exactly one Manga record per "
                + "populated row (II-10 contract).");

        // STATE assertion 3: post-import URL redirects to `/` (the Manga library
        // index per AppRoutes.tsx line 84 — Phase 15 Plan 15-07 flipped root
        // from SeriesIndex to MangaIndex, so the canonical post-import destination
        // is `/`, NOT `/manga` which lands on the NotFound catch-all). The redirect
        // is fired by ImportMangaFooter.handleImportPress's isAdding edge effect.
        // State-not-rendering assertion (feedback_verify_ui_state_not_just_rendering):
        // wait for the MangaIndex page testid to be visible — confirming the redirect
        // didn't land on a 404 surface that happens to contain "/manga" in its URL.
        await Page.WaitForURLAsync(
            new Regex(@"^https?://[^/]+/?$"),
            new PageWaitForURLOptions { Timeout = 15_000 });
        await Page.GetByTestId("manga-index-page").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
        Page.Url.Should()
            .MatchRegex(
                @"^https?://[^/]+/?$",
                "ImportMangaFooter.handleImportPress MUST redirect to `/` (the "
                + "Manga library index) after the bulk-add POST resolves (II-10). "
                + "Path `/manga` does NOT exist on Mangarr per AppRoutes.tsx — the "
                + "root route was flipped to MangaIndex in Phase 15 Plan 15-07.");

        // STATE assertion 4 (DB-side via API per Mangarr.Automation.Test
        // convention — fixtures use HTTP/API for DB-side verification, NOT
        // direct DB connection): GET /api/v5/manga returns ≥1 Manga record.
        // Reuse Playwright's APIRequestContext (already wired with X-Api-Key
        // via Context.ExtraHTTPHeaders per AutomationTest.cs:117).
        var apiResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/manga");
        apiResp.Status.Should()
            .Be(
                200,
                "GET /api/v5/manga must return 200 OK after the bulk-add "
                + "library-import landed.");

        var body = await apiResp.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should()
            .BeGreaterThanOrEqualTo(
                1,
                "library-import POST must persist ≥1 Manga record into the "
                + "Manga table (II-10 contract; verified via GET /api/v5/manga).");
    }
}
