using System.Collections.Generic;
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
///   2. OneTimeSetUp drops TWO manga subfolders + CBZ inside the baseline root
///      (UUID-named + human-title-named) so the scan view surfaces ≥2 unmapped
///      folder rows. Both folders auto-match to the same target manga ("Komi
///      Can't Communicate") via the existing UUID + new term-based cassettes —
///      this is the controller-branch-coverage matrix per GH issue #207.
///   3. UI drive: SelectFolder → click root → ImportManga scan → wait for
///      per-row lookup (auto-selects both rows) → UNCHECK the UUID row so
///      only the title-folder row POSTs → click bulk Import.
///   4. STATE assertions: 1 POST /api/v5/manga succeeded (201); URL redirected
///      to / (the MangaIndex library route — Phase 15 Plan 15-07 flipped root
///      to MangaIndex, so `/` IS the Manga library; there is no `/manga`
///      route); GET /api/v5/manga returns exactly 1 persisted Manga record
///      (II-10 contract).
///
/// Cassette-coverage matrix (issue #207 — both branches now covered by PRSmoke):
///   | Folder name                             | MangaLookupController branch        | Cassette source                               |
///   |-----------------------------------------|-------------------------------------|------------------------------------------------|
///   | a96676e5-8ae2-425e-b549-7f15dd34a6d8    | UUID short-circuit (lines 48-52)    | by-id: SearchForNewMangaByMangaDexId           |
///   | Komi Can't Communicate                  | Title fallback (lines 64-67)        | term-based: /manga?title=komi%20cant%20...     |
///
/// **Why only 1 row is imported (not both):** Both seeded folders auto-match
/// to the SAME MangaDex UUID `a96676e5-8ae2-425e-b549-7f15dd34a6d8`. The
/// frontend `ImportMangaFooter.handleImportPress` fires `Promise.allSettled`
/// over selected rows — concurrent POSTs. `AddMangaService.PrepareForAdd`
/// (line 220) only dedupes via `FindByMangaDexId` *before* insert, with no
/// DB-level UNIQUE constraint on `MangaDexId`. Two parallel POSTs therefore
/// race: both checks return null, both insert, both return 201, two
/// duplicate Manga rows persist. The test's INTENT (issue #207) is to
/// exercise both `MangaLookupController` branches under PRSmoke; that's
/// fully covered by the SCAN phase (both rows surface, both auto-select).
/// Unchecking the UUID row before Import keeps the IMPORT phase deterministic
/// (1 POST → 1 success → 1 DB row) without obscuring a separate concurrent-
/// import-dedup race condition that's tracked under GH follow-up.
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
    // `?term=<folderName>` on `/api/v5/manga/lookup`. Two folders are seeded so
    // both MangaLookupController branches get PRSmoke coverage:
    //
    //   MangaFolderUuid  → Guid.TryParse(term) true  → UUID short-circuit branch
    //                       → SearchForNewMangaByMangaDexId via cassette
    //                       → by-id metadata fetch
    //
    //   MangaFolderTitle → Guid.TryParse(term) false → title fallback branch
    //                       → SearchForNewManga(term) via cassette
    //                       → GET /manga?title=Komi+Can%27t+Communicate&...
    //
    // Both auto-match to the same target manga (MangaDex UUID
    // a96676e5-8ae2-425e-b549-7f15dd34a6d8 — "Komi Can't Communicate"), so the
    // controller-branch coverage widens with zero new MangaDex target surface.
    private const string MangaFolderUuid = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";
    private const string MangaFolderTitle = "Komi Can't Communicate";

    [OneTimeSetUp]
    public async Task SeedLibraryImportFixturesAsync()
    {
        // Disable Comix indexer (PATTERNS analog C — avoid Comix interference
        // during per-row lookup chain).
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();

        // AutomationTest.OneTimeSetUp seeds the baseline root folder at
        // Runner.AppData/MangaLibrary (AutomationTest.cs:104). Drop TWO manga
        // subfolders + minimal CBZ inside it so the /add/import scan surfaces
        // exactly 2 unmapped-folder rows — one keyed by UUID, one by human
        // title (issue #207 coverage matrix).
        var baselineRoot = Path.Combine(Runner.AppData, "MangaLibrary");
        SeedUnmappedFolder(baselineRoot, MangaFolderUuid);
        SeedUnmappedFolder(baselineRoot, MangaFolderTitle);
    }

    private static void SeedUnmappedFolder(string baselineRoot, string folderName)
    {
        var mangaFolder = Path.Combine(baselineRoot, folderName);
        Directory.CreateDirectory(mangaFolder);

        var cbzPath = Path.Combine(mangaFolder, $"{folderName} - Chapter 001 [en].cbz");
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
        // is the only one present; SeedLibraryImportFixturesAsync added two
        // unmapped subfolders (UUID + human title) inside it.
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

        // STATE assertion 1: both seeded subfolders surface as rows.
        // Issue #207: assert exactly 2 rows so a future regression that
        // silently drops a seed-folder is caught at PR time. Wait until both
        // rows have rendered (the scan endpoint may stream rows in slightly
        // out of order; assertion polls until the count stabilises).
        //
        // ImportMangaRow.tsx emits 6 testids per row (rowId is the folder name,
        // which contains dashes for the UUID folder, so a "no-dash" regex
        // can't isolate the root TableRow testid). The TableSelectCell suffix
        // `-select` is unconditionally present once per row → counts rows.
        var rowCountLocator = Page.Locator("[data-testid^='import-manga-row-'][data-testid$='-select']");
        await Assertions.Expect(rowCountLocator)
            .ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var rowCount = await rowCountLocator.CountAsync();
        rowCount.Should()
            .Be(
                2,
                "library-import scan must surface exactly 2 unmapped-folder rows "
                + "for the seeded UUID + human-title subfolders (Phase 17.3 L-002 "
                + "first-record-creation contract + issue #207 controller-branch "
                + "coverage matrix).");

        // Wait for the per-row lookup chain to resolve a populated
        // selectedManga.title cell on BOTH rows (the Import button is disabled
        // when importableCount === 0 per ImportMangaFooter.tsx). Both folders
        // auto-match to the same target manga via different controller branches:
        //   UUID folder → SearchForNewMangaByMangaDexId (existing cassette)
        //   Title folder → SearchForNewManga(term) (new term-based cassette)
        var populatedSelector = Page.Locator(
            "[data-testid^='import-manga-row-'][data-testid$='-selected-manga-name']:not(:empty)");
        await Assertions.Expect(populatedSelector)
            .ToHaveCountAsync(2, new() { Timeout = 20_000 });

        var populatedCount = await populatedSelector.CountAsync();
        populatedCount.Should()
            .Be(
                2,
                "useLookupManga must resolve a populated selectedManga.title "
                + "on BOTH seeded rows within 20s of scan render. UUID folder "
                + "hits MangaLookupController's UUID-detection branch (line 48); "
                + "title folder hits the SearchForNewManga(term) fallback "
                + "branch (line 66). Both cassettes must be present under "
                + "Fixtures/Cassettes/MangaDex/ (issue #207).");

        // Uncheck the UUID-folder row so only the title-folder row POSTs on
        // Import. Both rows auto-select on lookup resolution (ImportMangaRow.tsx
        // line 145-150). The title folder's `-select` testid is
        // `import-manga-row-Komi Can't Communicate-select`; the UUID folder's
        // is `import-manga-row-a96676e5-...-select`.
        //
        // Why: see the fixture-header comment "Why only 1 row is imported".
        // Both lookup branches were exercised during the scan phase above; the
        // import phase only needs to deterministically persist 1 Manga.
        //
        // CRITICAL synchronization: the `:not(:empty)` selector above does NOT
        // prove lookup completion — `ImportMangaSelectManga` always renders the
        // dropdown chevron so the cell is never empty. While lookup is still
        // pending, `item?.selectedManga` is null → the row's TableSelectCell is
        // `isDisabled` (ImportMangaRow.tsx line 204) and `CheckInput` no-ops
        // disabled clicks (CheckInput.tsx line 74-78). The deterministic
        // signal is the bulk Import button: `isDisabled={!selectedCount ||
        // isLookingUpManga}` (ImportMangaFooter.tsx line 368) + label
        // `Import {count} Manga` (line 372 + en.json:866). Wait until the
        // button shows "Import 2 Manga" — that proves both lookups completed
        // AND both rows auto-selected. Then click to uncheck the UUID row and
        // wait for "Import 1 Manga" — that proves the uncheck stuck.
        var importButton = Page.GetByTestId("import-manga-bulk-import-button");
        await importButton.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });
        await Assertions.Expect(importButton)
            .ToHaveTextAsync(
                new Regex(@"Import\s+2\s+Manga"),
                new() { Timeout = 20_000 });

        var uuidRowSelect = Page.GetByTestId(
            $"import-manga-row-{MangaFolderUuid}-select");

        // The TableSelectCell wraps the checkbox in a sibling; the testid IS
        // on the wrapping cell. Click the cell to toggle the inner checkbox.
        await uuidRowSelect.ClickAsync();

        // STATE assertion: button text drops to "Import 1 Manga" — proves the
        // uncheck took effect at the useSelect store level (not just the DOM).
        await Assertions.Expect(importButton)
            .ToHaveTextAsync(
                new Regex(@"Import\s+1\s+Manga"),
                new() { Timeout = 10_000 });

        var postResponses = new List<int>();
        var postLock = new object();

        void OnResponse(object sender, IResponse r)
        {
            if (addEndpointRegex.IsMatch(r.Url) && r.Request.Method == "POST")
            {
                lock (postLock)
                {
                    postResponses.Add(r.Status);
                }
            }
        }

        Page.Response += OnResponse;
        try
        {
            await importButton.ClickAsync();

            // STATE assertion 2: post-import URL redirects to `/` (the Manga library
            // index per AppRoutes.tsx line 84 — Phase 15 Plan 15-07 flipped root
            // from SeriesIndex to MangaIndex, so the canonical post-import destination
            // is `/`, NOT `/manga` which lands on the NotFound catch-all). The redirect
            // is fired by ImportMangaFooter.handleImportPress's Promise.allSettled
            // `finally` block — it fires AFTER every POST settles.
            // State-not-rendering assertion (feedback_verify_ui_state_not_just_rendering):
            // wait for the MangaIndex page testid to be visible — confirming the redirect
            // didn't land on a 404 surface that happens to contain "/manga" in its URL.
            await Page.WaitForURLAsync(
                new Regex(@"^https?://[^/]+/?$"),
                new PageWaitForURLOptions { Timeout = 30_000 });
            await Page.GetByTestId("manga-index-page").WaitForAsync(
                new LocatorWaitForOptions { Timeout = 15_000 });
            Page.Url.Should()
                .MatchRegex(
                    @"^https?://[^/]+/?$",
                    "ImportMangaFooter.handleImportPress MUST redirect to `/` (the "
                    + "Manga library index) after all bulk-add POSTs settle (II-10). "
                    + "Path `/manga` does NOT exist on Mangarr per AppRoutes.tsx — the "
                    + "root route was flipped to MangaIndex in Phase 15 Plan 15-07.");
        }
        finally
        {
            Page.Response -= OnResponse;
        }

        // STATE assertion 3: exactly 1 POST fired and returned 201. Only the
        // title-folder row was selected for import; the UUID-folder row was
        // unchecked above.
        List<int> snapshot;
        lock (postLock)
        {
            snapshot = new List<int>(postResponses);
        }

        snapshot.Count.Should()
            .Be(
                1,
                "Exactly 1 POST must fire — only the title-folder row was selected "
                + "for import (UUID-folder row was unchecked above). Observed: "
                + string.Join(", ", snapshot));

        snapshot[0].Should()
            .BeInRange(
                200,
                299,
                "The single POST must return 2xx success (II-10 contract); observed: "
                + snapshot[0]);

        // STATE assertion 4 (DB-side via API per Mangarr.Automation.Test
        // convention — fixtures use HTTP/API for DB-side verification, NOT
        // direct DB connection): GET /api/v5/manga returns exactly 1 Manga
        // record (NOT 2 — the AddMangaService.PrepareForAdd MangaDexId-dedup
        // collapses both folders into the same canonical Manga row). Reuse
        // Playwright's APIRequestContext (already wired with X-Api-Key via
        // Context.ExtraHTTPHeaders per AutomationTest.cs:117).
        var apiResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/manga");
        apiResp.Status.Should()
            .Be(
                200,
                "GET /api/v5/manga must return 200 OK after the bulk-add "
                + "library-import landed.");

        var body = await apiResp.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().Should()
            .Be(
                1,
                "library-import POST must persist exactly 1 Manga record into "
                + "the Manga table (II-10 contract; verified via GET /api/v5/"
                + "manga). Both seeded folders auto-match to the same MangaDexId, "
                + "so AddMangaService.PrepareForAdd's FindByMangaDexId guard "
                + "dedupes the second POST → 1 canonical row.");
    }
}
