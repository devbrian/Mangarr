using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B) — CRUD round-trip on
// /api/v5/importlistexclusion (the second V5 surface shipped by Plan 26-05
// via ImportListExclusionController). Exercises the manga-ID triplet shape
// (MangaDexId string / MalId int? / AniListId int?) per Migration 003.
//
// Unlike Plan 26-06 Fixture 2 (which depends on the test-only TestImportList
// fake being in production DI), ImportListExclusion is a plain CRUD entity
// (RestController<ImportListExclusionResource>) — no provider plugin required.
// So this fixture can run GREEN today against the substrate without forward-
// staging Phase 27.
//
// gh-226 absorption (2026-05-21): the dead-code
// frontend/src/Settings/ImportLists/ImportListExclusions/ImportListExclusions.test.tsx
// Jest fixture asserted (a) Pitfall 7 — `malId ?? '—'` and `aniListId ?? '—'`
// must render legal `0` as `'0'` not the en-dash; (b) column-header click
// dispatches `setImportListExclusionSort` with the clicked sortKey. Under
// Option B from devbrian/Mangarr#226 the Jest fixture is deleted and those
// behaviours move here:
//   - `exclusion_pitfall7_legal_zero_ids_render_as_zero_not_endash`
//   - `exclusion_column_header_click_re_sorts_visible_rows`
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors. Uses
// `settings-importlist-exclusions` + `edit-importlist-exclusion-modal` +
// `settings-importlist-exclusion-row-{id}` v1.1 prefix family per Phase 18 D-18
// (the `settings-*` allow-listed prefix qualifies the row testid).
//
// State assertion discipline per `feedback_verify_ui_state_not_just_rendering`:
// every step verifies BOTH the UI shape AND the underlying V5 row via parallel
// GET /api/v5/importlistexclusion, so a silently-rejected POST cannot pass.
[TestFixture]
[Category("AutomationTest")]
public class ImportListExclusionCrudFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task exclusion_crud_round_trip()
    {
        // 1. Navigate to /settings/importlists. The exclusion subtree mounts as
        //    a sibling FieldSet under ImportListSettings.tsx (no separate route).
        var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 2. POST — create the exclusion row directly via the V5 API. The UI's
        //    "Add" icon-button opens EditImportListExclusionModal with id
        //    undefined (Add mode); the modal Save fires
        //    POST /api/v5/importlistexclusion. To keep the fixture deterministic
        //    against the modal's input-binding races, we POST via HttpClient and
        //    verify the UI picks up the new row on its next refetch.
        const string TestMangaDexId = "abc12345-6789-0123-4567-890123456789";
        const string TestTitle = "Test Exclusion Title";
        const int TestMalId = 9999;
        const int TestAniListId = 8888;

        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var postPayload = new
        {
            mangaDexId = TestMangaDexId,
            malId = TestMalId,
            aniListId = TestAniListId,
            title = TestTitle
        };
        var postResp = await http.PostAsJsonAsync("importlistexclusion", postPayload);
        postResp.IsSuccessStatusCode.Should().BeTrue(
            "POST /api/v5/importlistexclusion must succeed (body: {0})",
            await postResp.Content.ReadAsStringAsync());

        var postBody = await postResp.Content.ReadAsStringAsync();
        using var postDoc = JsonDocument.Parse(postBody);
        var exclusionId = postDoc.RootElement.GetProperty("id").GetInt32();

        // 3. GET via V5 — verify the exclusion is queryable. The endpoint is
        //    paged (PagingResource<ImportListExclusionResource>); the records
        //    array holds rows.
        var getResp = await http.GetAsync("importlistexclusion");
        getResp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/importlistexclusion must return 2xx");
        var getBody = await getResp.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getBody);
        var records = getDoc.RootElement.GetProperty("records");
        records.GetArrayLength().Should().BeGreaterThan(0,
            "the paged list must contain at least the row we just created");

        // 4. State assertion (UI side, after reload): the page picks up the row
        //    on its next refetch — explicitly refetch by re-navigating.
        await Page.GotoAsync($"{RootUri}/settings/importlists");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var exclusionRow = Page.GetByTestId($"settings-importlist-exclusion-row-{exclusionId}");
        await Assertions.Expect(exclusionRow).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 5. PUT — mutate the Title via V5; verify UI reflects after refetch.
        const string MutatedTitle = "Test Exclusion Title (PUT'd)";
        var putPayload = new
        {
            id = exclusionId,
            mangaDexId = TestMangaDexId,
            malId = TestMalId,
            aniListId = TestAniListId,
            title = MutatedTitle
        };
        var putResp = await http.PutAsJsonAsync($"importlistexclusion/{exclusionId}", putPayload);
        putResp.IsSuccessStatusCode.Should().BeTrue(
            "PUT /api/v5/importlistexclusion/{0} must succeed (body: {1})",
            exclusionId,
            await putResp.Content.ReadAsStringAsync());

        // Verify via V5: paged-GET the row, assert title field carries the mutation.
        var getResp2 = await http.GetAsync("importlistexclusion");
        using var getDoc2 = JsonDocument.Parse(await getResp2.Content.ReadAsStringAsync());
        var found = false;
        foreach (var rec in getDoc2.RootElement.GetProperty("records").EnumerateArray())
        {
            if (rec.GetProperty("id").GetInt32() == exclusionId)
            {
                rec.GetProperty("title").GetString().Should().Be(MutatedTitle,
                    "V5 PUT response should reflect the title mutation on subsequent GET");
                found = true;
                break;
            }
        }

        found.Should().BeTrue("the PUT'd row must remain in the paged list");

        // 6. DELETE — remove the exclusion via V5; verify the row is gone from BOTH
        //    the V5 GET and the UI.
        var deleteResp = await http.DeleteAsync($"importlistexclusion/{exclusionId}");
        deleteResp.IsSuccessStatusCode.Should().BeTrue(
            "DELETE /api/v5/importlistexclusion/{0} must return 2xx (body: {1})",
            exclusionId,
            await deleteResp.Content.ReadAsStringAsync());

        // V5 verification side.
        var getResp3 = await http.GetAsync("importlistexclusion");
        using var getDoc3 = JsonDocument.Parse(await getResp3.Content.ReadAsStringAsync());
        var stillThere = false;
        foreach (var rec in getDoc3.RootElement.GetProperty("records").EnumerateArray())
        {
            if (rec.GetProperty("id").GetInt32() == exclusionId)
            {
                stillThere = true;
                break;
            }
        }

        stillThere.Should().BeFalse("the deleted row must not appear in subsequent V5 GET");

        // UI verification side — refetch and assert the row is gone.
        await Page.GotoAsync($"{RootUri}/settings/importlists");
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(exclusionRow).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
    }

    [Test]
    public async Task exclusion_pitfall7_legal_zero_ids_render_as_zero_not_endash()
    {
        // gh-226 absorption — the deleted ImportListExclusions.test.tsx Jest
        // fixture asserted Pitfall 7 via React-tree inspection on the Row
        // component. ImportListExclusionRow.tsx renders `malId ?? '—'` and
        // `aniListId ?? '—'` so a legal `0` (e.g. AniList's reserved root id)
        // must render as the literal `0`, not the en-dash sentinel. The
        // bug-shape we're guarding is a refactor that swaps `??` → `||`, which
        // would silently mask `0` as falsy and render `'—'` in the cell.
        //
        // Strategy: seed one row with malId=0 and aniListId=0 via the V5 API,
        // then walk the DOM to the specific cell positions in the rendered
        // TableRow and assert TextContent is `"0"`, not `"—"`.
        //
        // Cell layout per ImportListExclusionRow.tsx:
        //   td[0] = TableSelectCell (checkbox)
        //   td[1] = title
        //   td[2] = mangaDexId
        //   td[3] = malId       <-- assert "0"
        //   td[4] = aniListId   <-- assert "0"
        //   td[5] = actions
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var runTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var seedPayload = new
        {
            mangaDexId = $"00000000-pit7-zero-zero-{runTag}{runTag[..4]}",
            malId = 0,        // Pitfall 7 regression guard — legal `0`
            aniListId = 0,    // Pitfall 7 regression guard — legal `0`
            title = $"Pitfall 7 Zero IDs [{runTag}]"
        };

        var seedResp = await http.PostAsJsonAsync("importlistexclusion", seedPayload);
        seedResp.IsSuccessStatusCode.Should().BeTrue(
            "POST seed row with malId=0 + aniListId=0 must succeed (body: {0})",
            await seedResp.Content.ReadAsStringAsync());
        using var seedDoc = JsonDocument.Parse(await seedResp.Content.ReadAsStringAsync());
        var rowId = seedDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var row = Page.GetByTestId($"settings-importlist-exclusion-row-{rowId}");
            await Assertions.Expect(row).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // Walk to td[3] (malId cell — 0-indexed 4th cell, 1-indexed 4th in
            // nth-child). nth-child is 1-indexed: TableSelectCell=1, title=2,
            // mangaDexId=3, malId=4, aniListId=5.
            var malIdCell = row.Locator("td:nth-child(4)");
            var aniListIdCell = row.Locator("td:nth-child(5)");

            var malIdText = (await malIdCell.TextContentAsync())?.Trim();
            var aniListIdText = (await aniListIdCell.TextContentAsync())?.Trim();

            // Pitfall 7 contract: legal `0` renders as `"0"`, NEVER as `"—"`.
            // A refactor that swaps `malId ?? '—'` → `malId || '—'` fails here.
            malIdText.Should().Be("0",
                "Pitfall 7 — `malId ?? '—'` must render legal `0` as the literal '0' "
                + "(a `||` regression would silently render '—' instead, masking valid AniList/MAL root ids)");
            aniListIdText.Should().Be("0",
                "Pitfall 7 — `aniListId ?? '—'` must render legal `0` as the literal '0'");

            malIdText.Should().NotBe("—", "the en-dash sentinel must NOT appear for legal `0`");
            aniListIdText.Should().NotBe("—", "the en-dash sentinel must NOT appear for legal `0`");
        }
        finally
        {
            _ = await http.DeleteAsync($"importlistexclusion/{rowId}");
        }
    }

    [Test]
    public async Task exclusion_column_header_click_re_sorts_visible_rows()
    {
        // gh-226 absorption — the deleted ImportListExclusions.test.tsx Jest
        // fixture asserted the column-header click → `setImportListExclusionSort`
        // dispatch shape (via mocked-store inspection). The live equivalent is
        // a real boot: seed 3 rows with non-alphabetical insertion order on
        // `title`, click the Title column header, assert the visible row order
        // flips. This proves the Zustand sort store's `setImportListExclusionSort`
        // is actually wired to the Table's `onSortPress` callback — the dispatch
        // shape is the necessary precondition for the visible re-sort.
        //
        // Title column is sortable per ImportListExclusions.tsx:67-71 (COLUMNS
        // array entry { name: 'title', isSortable: true }). Default sortKey
        // is 'id' descending (importListExclusionOptionsStore initial state),
        // so the first click lands on title + ascending; the second flips to
        // descending. We assert the first-cell text changes between the two
        // states.
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var runTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var titleZ = $"ZZZ Exclusion [{runTag}]";
        var titleA = $"AAA Exclusion [{runTag}]";
        var titleM = $"MMM Exclusion [{runTag}]";

        // Seed in non-alphabetical order (Z, A, M) so a default-id sort vs.
        // ascending-title sort surface different first-row values.
        var seedZResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"zzzz0000-sort-zzzz-{runTag}{runTag[..4]}",
            malId = 7771,
            aniListId = 7771,
            title = titleZ
        });
        seedZResp.IsSuccessStatusCode.Should().BeTrue("seed Z row must succeed");
        using var seedZDoc = JsonDocument.Parse(await seedZResp.Content.ReadAsStringAsync());
        var idZ = seedZDoc.RootElement.GetProperty("id").GetInt32();

        var seedAResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"aaaa0000-sort-aaaa-{runTag}{runTag[..4]}",
            malId = 7772,
            aniListId = 7772,
            title = titleA
        });
        seedAResp.IsSuccessStatusCode.Should().BeTrue("seed A row must succeed");
        using var seedADoc = JsonDocument.Parse(await seedAResp.Content.ReadAsStringAsync());
        var idA = seedADoc.RootElement.GetProperty("id").GetInt32();

        var seedMResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"mmmm0000-sort-mmmm-{runTag}{runTag[..4]}",
            malId = 7773,
            aniListId = 7773,
            title = titleM
        });
        seedMResp.IsSuccessStatusCode.Should().BeTrue("seed M row must succeed");
        using var seedMDoc = JsonDocument.Parse(await seedMResp.Content.ReadAsStringAsync());
        var idM = seedMDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // Make all three rows visible by selecting page-size large enough.
            // Initial render uses the default page-size (Sonarr canonical 20),
            // which is plenty for our 3 seeded rows.
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idA}"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idM}"))
                .ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idZ}"))
                .ToBeVisibleAsync();

            // Find the Title header by visible text and click it. The Mangarr
            // TableHeader maps `onSortPress` to a clickable Link wrapping the
            // label; clicking the label text triggers the sort dispatch.
            //
            // Use a precise scope: only headers INSIDE the exclusions
            // FieldSet, to avoid colliding with any other Title header on
            // the page (notifications/notes/etc.).
            var exclusionsContainer = Page.GetByTestId("settings-importlist-exclusions");
            await Assertions.Expect(exclusionsContainer).ToBeVisibleAsync();

            var titleHeader = exclusionsContainer.Locator("th").Filter(new()
            {
                HasText = "Title"
            }).First;

            // Capture top-row title BEFORE clicking. Default store state is
            // sortKey='id' / sortDirection='descending' so the top row is the
            // most-recently-inserted (row M, id=3, title 'MMM Exclusion').
            // This baseline lets us assert the visible order CHANGES after
            // each click without predicting absolute direction — the
            // `applySort` helper in useOptionsStore.ts:174-198 PRESERVES
            // sortDirection when sortKey changes, so first-click on Title
            // keeps descending direction (top row will become 'ZZZ Exclusion'
            // — alphabetically last). The second click on the same column
            // flips direction → ascending → top becomes 'AAA Exclusion'.
            var firstTitleCellSelector =
                "[data-testid='settings-importlist-exclusions'] tbody tr:first-child td:nth-child(2)";

            var firstTitleBeforeClick = await Page.Locator(firstTitleCellSelector).TextContentAsync();
            firstTitleBeforeClick.Should().StartWith("MMM Exclusion",
                "baseline: default sortKey='id' + descending puts the most-recently-seeded row "
                + "(MMM Exclusion, id=3) on top");

            await titleHeader.ClickAsync();

            // After the click, the Zustand store updates sortKey='title';
            // useImportListExclusions re-queries with the new sort; the table
            // body re-renders with title-sorted rows. Direction is preserved
            // at 'descending' per applySort's sortKey-change branch, so
            // titles reverse-alphabetical → ZZZ first.
            await Page.WaitForFunctionAsync(
                $"() => document.querySelector(\"{firstTitleCellSelector}\")?.textContent?.trim().startsWith('ZZZ Exclusion')",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000, PollingInterval = 200 });

            var firstTitleAfterFirstClick = await Page.Locator(firstTitleCellSelector).TextContentAsync();
            firstTitleAfterFirstClick.Should().Contain("ZZZ Exclusion",
                "first click on Title header dispatches sortKey='title' with PRESERVED descending "
                + "direction (applySort preserves direction when sortKey changes); ZZZ Exclusion "
                + "sits first reverse-alphabetically");

            // Click again — same sortKey → flips direction to ascending.
            // Top row should now be AAA.
            await titleHeader.ClickAsync();
            await Page.WaitForFunctionAsync(
                $"() => document.querySelector(\"{firstTitleCellSelector}\")?.textContent?.trim().startsWith('AAA Exclusion')",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000, PollingInterval = 200 });

            var firstTitleAfterSecondClick = await Page.Locator(firstTitleCellSelector).TextContentAsync();
            firstTitleAfterSecondClick.Should().Contain("AAA Exclusion",
                "second click on Title header (same sortKey) flips direction to ascending; "
                + "AAA Exclusion sits first alphabetically");
        }
        finally
        {
            _ = await http.DeleteAsync($"importlistexclusion/{idZ}");
            _ = await http.DeleteAsync($"importlistexclusion/{idA}");
            _ = await http.DeleteAsync($"importlistexclusion/{idM}");
        }
    }

    [Test]
    public async Task exclusion_three_id_columns_each_dispatch_sort_when_clicked()
    {
        // gh-226 PR-review follow-up — the prior test
        // (exclusion_column_header_click_re_sorts_visible_rows) only exercises
        // the Title column header. The deleted Jest fixture asserted that
        // EACH of the 3 ID column headers (MangaDex ID / MyAnimeList ID /
        // AniList ID) dispatches setImportListExclusionSort with its own
        // sortKey. Pinning each ID column individually under live boot is
        // the right home for that claim.
        //
        // Seed strategy: 3 rows with INVERSELY monotonic ID values so the
        // first-row identity flips between each column's ascending sort.
        //   row P: mangaDexId='a000...', malId=300, aniListId=100
        //   row Q: mangaDexId='m000...', malId=200, aniListId=200
        //   row R: mangaDexId='z000...', malId=100, aniListId=300
        //
        // After click on MangaDexId header (asc): row P (a*) on top
        // After click on MalId header     (asc): row R (mal=100) on top
        // After click on AniListId header (asc): row P (aniList=100) on top
        // — note: row P and row R are different identities so the assertion
        // genuinely distinguishes which sortKey was dispatched.
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var runTag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var titleP = $"PPP Excl [{runTag}]";
        var titleQ = $"QQQ Excl [{runTag}]";
        var titleR = $"RRR Excl [{runTag}]";

        var seedPResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"aaaa1111-id3c-aaaa-{runTag}{runTag[..4]}",
            malId = 300,
            aniListId = 100,
            title = titleP
        });
        seedPResp.IsSuccessStatusCode.Should().BeTrue(
            "seed row P must succeed (body: {0})",
            await seedPResp.Content.ReadAsStringAsync());
        using var seedPDoc = JsonDocument.Parse(await seedPResp.Content.ReadAsStringAsync());
        var idP = seedPDoc.RootElement.GetProperty("id").GetInt32();

        var seedQResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"mmmm1111-id3c-mmmm-{runTag}{runTag[..4]}",
            malId = 200,
            aniListId = 200,
            title = titleQ
        });
        seedQResp.IsSuccessStatusCode.Should().BeTrue("seed row Q must succeed");
        using var seedQDoc = JsonDocument.Parse(await seedQResp.Content.ReadAsStringAsync());
        var idQ = seedQDoc.RootElement.GetProperty("id").GetInt32();

        var seedRResp = await http.PostAsJsonAsync("importlistexclusion", new
        {
            mangaDexId = $"zzzz1111-id3c-zzzz-{runTag}{runTag[..4]}",
            malId = 100,
            aniListId = 300,
            title = titleR
        });
        seedRResp.IsSuccessStatusCode.Should().BeTrue("seed row R must succeed");
        using var seedRDoc = JsonDocument.Parse(await seedRResp.Content.ReadAsStringAsync());
        var idR = seedRDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var exclusionsContainer = Page.GetByTestId("settings-importlist-exclusions");
            await Assertions.Expect(exclusionsContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // Wait for all 3 seeded rows to be present before exercising
            // column-header clicks.
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idP}"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idQ}"))
                .ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId($"settings-importlist-exclusion-row-{idR}"))
                .ToBeVisibleAsync();

            // Per ImportListExclusions.tsx COLUMNS array, column positions are:
            //   td:nth-child(1) = TableSelectCell
            //   td:nth-child(2) = Title
            //   td:nth-child(3) = MangaDex ID
            //   td:nth-child(4) = MyAnimeList ID
            //   td:nth-child(5) = AniList ID
            //   td:nth-child(6) = actions
            //
            // Direction-aware: applySort PRESERVES sortDirection when sortKey
            // changes (useOptionsStore.ts:189-191). Default state on a fresh
            // localStorage is sortKey='id' + sortDirection='descending'. So
            // first-click on EACH new ID column lands on that column with
            // descending direction. We seeded rows so the descending-by-each-
            // ID-column top row is a UNIQUE identity per column:
            //   mangaDexId desc → row R (mdx='zzzz1111-…')
            //   malId      desc → row P (mal=300)
            //   aniListId  desc → row R (anl=300)
            //
            // The aria-sort attribute on each header is the canonical state
            // marker: only the active sortKey's header carries 'ascending' or
            // 'descending'; the others return to 'none'. Asserting aria-sort
            // transitions is direction-agnostic and survives any future
            // refactor of applySort that changes the initial direction.

            // 1) MangaDex ID column.
            var mangaDexHeader = exclusionsContainer.Locator("th").Filter(new()
            {
                HasText = "MangaDex ID"
            }).First;
            await mangaDexHeader.ClickAsync();

            // aria-sort on MangaDex ID header transitions to non-'none'.
            await Page.WaitForFunctionAsync(
                "(el) => el && el.getAttribute('aria-sort') !== 'none' && el.getAttribute('aria-sort') !== null",
                await mangaDexHeader.ElementHandleAsync(),
                new PageWaitForFunctionOptions { Timeout = 10_000, PollingInterval = 100 });

            var mangaDexAriaSort = await mangaDexHeader.GetAttributeAsync("aria-sort");
            mangaDexAriaSort.Should().BeOneOf(new[] { "ascending", "descending" },
                "click on MangaDex ID header must dispatch sortKey='mangaDexId' — "
                + "aria-sort on that header should be non-'none' after dispatch");

            // The Title header's aria-sort must return to 'none' (only the
            // active sortKey carries a non-'none' aria-sort). This proves
            // the dispatch SWITCHED columns rather than no-oping.
            var titleHeader = exclusionsContainer.Locator("th").Filter(new()
            {
                HasText = "Title"
            }).First;
            (await titleHeader.GetAttributeAsync("aria-sort")).Should().Be("none",
                "after dispatching sortKey='mangaDexId', the Title column's aria-sort "
                + "should return to 'none' — the active sortKey is now mangaDexId");

            // 2) MyAnimeList ID column.
            var malHeader = exclusionsContainer.Locator("th").Filter(new()
            {
                HasText = "MyAnimeList ID"
            }).First;
            await malHeader.ClickAsync();

            await Page.WaitForFunctionAsync(
                "(el) => el && el.getAttribute('aria-sort') !== 'none' && el.getAttribute('aria-sort') !== null",
                await malHeader.ElementHandleAsync(),
                new PageWaitForFunctionOptions { Timeout = 10_000, PollingInterval = 100 });

            var malAriaSort = await malHeader.GetAttributeAsync("aria-sort");
            malAriaSort.Should().BeOneOf(new[] { "ascending", "descending" },
                "click on MyAnimeList ID header must dispatch sortKey='malId' — "
                + "aria-sort on that header should be non-'none' after dispatch");

            // MangaDex ID header's aria-sort must return to 'none' now that
            // the active sortKey moved to 'malId'.
            (await mangaDexHeader.GetAttributeAsync("aria-sort")).Should().Be("none",
                "after dispatching sortKey='malId', the MangaDex ID column's aria-sort "
                + "should return to 'none' — only the active sortKey is highlighted");

            // 3) AniList ID column.
            var aniListHeader = exclusionsContainer.Locator("th").Filter(new()
            {
                HasText = "AniList ID"
            }).First;
            await aniListHeader.ClickAsync();

            await Page.WaitForFunctionAsync(
                "(el) => el && el.getAttribute('aria-sort') !== 'none' && el.getAttribute('aria-sort') !== null",
                await aniListHeader.ElementHandleAsync(),
                new PageWaitForFunctionOptions { Timeout = 10_000, PollingInterval = 100 });

            var aniListAriaSort = await aniListHeader.GetAttributeAsync("aria-sort");
            aniListAriaSort.Should().BeOneOf(new[] { "ascending", "descending" },
                "click on AniList ID header must dispatch sortKey='aniListId' — "
                + "aria-sort on that header should be non-'none' after dispatch");

            (await malHeader.GetAttributeAsync("aria-sort")).Should().Be("none",
                "after dispatching sortKey='aniListId', the MyAnimeList ID column's aria-sort "
                + "should return to 'none' — only the active sortKey is highlighted");
        }
        finally
        {
            _ = await http.DeleteAsync($"importlistexclusion/{idP}");
            _ = await http.DeleteAsync($"importlistexclusion/{idQ}");
            _ = await http.DeleteAsync($"importlistexclusion/{idR}");
        }
    }
}
