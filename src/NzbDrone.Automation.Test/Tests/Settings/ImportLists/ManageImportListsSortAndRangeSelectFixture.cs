using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

/// <summary>
/// GH #224 regression — Manage Import Lists modal sort + shift-click range-
/// select. Mirrors <c>ManageIndexersSortAndRangeSelectFixture</c> (the parent
/// bug surfaced first on the Indexers peer and was ported verbatim into the
/// ImportLists peer in Phase 27.1 Plan 27.1-04 per the verbatim-port mandate
/// in D-03).
///
/// Pre-fix bug shape:
///   - <c>useSortedImportLists</c> hardcoded <c>sortByProp('name')</c> while
///     the table's column-header click dispatched into the Zustand
///     <c>useManageImportListsOptions</c> store (silently ignored).
///   - <c>&lt;SelectProvider items={useImportListsData()}&gt;</c> received the
///     unsorted raw cache while the inner <c>&lt;Table&gt;</c> rendered sorted
///     data. Shift-click range-select therefore toggled the contiguous block
///     of the UNSORTED array, which differs from what the user saw.
///
/// Seed strategy is identical to the Indexers peer: 3 rows in non-alphabetical
/// insertion order (CCC, AAA, BBB) so the unsorted raw cache disagrees with
/// the visible (name-sorted) order.
///
/// gh-226 absorption (2026-05-21): the dead-code
/// frontend/src/Settings/ImportLists/ImportLists/Manage/ManageImportListsModalContent.test.tsx
/// Jest fixture asserted the COLUMNS array shape (6 manga columns: name,
/// implementation, enableAutomaticAdd, rootFolderPath, translationProfileId,
/// tags) and that the 5 forbidden Sonarr-TV columns (protocol, enableRss,
/// enableAutomaticSearch, enableInteractiveSearch, priority) are absent. Under
/// Option B from devbrian/Mangarr#226 the Jest fixture is deleted and that
/// behaviour moves into the second test method here:
/// <c>manage_modal_renders_six_manga_columns_no_sonarr_tv_columns</c>.
///
/// Tier: nightly (no PRSmoke).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ManageImportListsSortAndRangeSelectFixture : AutomationTest
{
    private const string Name_A = "AAA List";
    private const string Name_B = "BBB List";
    private const string Name_C = "CCC List";

    private int _idC;
    private int _idA;
    private int _idB;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var kit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);

#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await kit.DisableComixIndexerAsync();
#pragma warning restore CS0618

        // Seed 3 MangaDex import-list rows in NON-alphabetical insertion order
        // (C, A, B). See class-level doc-comment for why this matters.
        var (_, defC) = await kit.RegisterMangaDexImportListAsync(Name_C);
        var (_, defA) = await kit.RegisterMangaDexImportListAsync(Name_A);
        var (_, defB) = await kit.RegisterMangaDexImportListAsync(Name_B);

        defC.Should().NotBeNull("MangaDex provider seed should succeed for {0}", Name_C);
        defA.Should().NotBeNull("MangaDex provider seed should succeed for {0}", Name_A);
        defB.Should().NotBeNull("MangaDex provider seed should succeed for {0}", Name_B);

        _idC = defC!.Value;
        _idA = defA!.Value;
        _idB = defB!.Value;
    }

    [Test]
    public async Task column_header_click_resorts_visible_rows_and_shift_click_range_selects_visible_block()
    {
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(page.ManageButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await page.ManageButton.ClickAsync();

        var manageContent = Page.GetByTestId("manage-importlists-modal-content");
        await Assertions.Expect(manageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var rowA = manageContent.GetByTestId($"settings-importlist-row-{_idA}");
        var rowB = manageContent.GetByTestId($"settings-importlist-row-{_idB}");
        var rowC = manageContent.GetByTestId($"settings-importlist-row-{_idC}");

        await Assertions.Expect(rowA).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(rowB).ToBeVisibleAsync();
        await Assertions.Expect(rowC).ToBeVisibleAsync();

        // 1. Default sort = name ascending. Visible first row's name cell should
        //    contain 'AAA List'.
        var firstRowNameSelector =
            "[data-testid='manage-importlists-modal-content'] tbody tr td:nth-child(2)";

        var firstRowName0 = await Page.Locator(firstRowNameSelector).First.TextContentAsync();
        firstRowName0.Should().Contain(Name_A,
            "default sortKey=name + sortDirection=ascending should put '{0}' on top",
            Name_A);

        // 2. Click the Name column header → descending. Visible first row flips
        //    to 'CCC List'. Header carries data-testid="settings-importlist-name-header"
        //    (plumbed through Column → Table → TableHeaderCell → Link); the testid
        //    contract per D-18 keeps this fixture decoupled from DOM structure.
        var nameHeader = Page.GetByTestId("settings-importlist-name-header");
        await nameHeader.ClickAsync();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelector(\"{firstRowNameSelector}\")?.textContent?.trim().startsWith('CCC')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var nameAriaSortDesc = await nameHeader.GetAttributeAsync("aria-sort");
        nameAriaSortDesc.Should().Be("descending");

        // 3. Click Name again → ascending. Visible first row returns to 'AAA'.
        await nameHeader.ClickAsync();
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector(\"{firstRowNameSelector}\")?.textContent?.trim().startsWith('AAA')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var nameAriaSortAsc = await nameHeader.GetAttributeAsync("aria-sort");
        nameAriaSortAsc.Should().Be("ascending");

        // 4. Shift-click range-select. Visible order = [AAA, BBB, CCC].
        //    Pre-fix: SelectProvider items = unsorted [CCC, AAA, BBB]. AAA at
        //    unsorted-index 1, CCC at unsorted-index 0. Range [0..1] selects
        //    only {CCC, AAA} = 2 rows.
        //    Post-fix: SelectProvider items == sorted table array. Range
        //    AAA→CCC selects {AAA, BBB, CCC} = 3 rows.
        var rowA_checkbox = manageContent.GetByTestId(
            $"settings-importlist-row-{_idA}-checkbox");
        var rowC_checkbox = manageContent.GetByTestId(
            $"settings-importlist-row-{_idC}-checkbox");

        await rowA_checkbox.ClickAsync();
        await rowC_checkbox.ClickAsync(new LocatorClickOptions { Modifiers = new[] { KeyboardModifier.Shift } });

        var checkedInputs = Page.Locator(
            "[data-testid='manage-importlists-modal-content'] tbody input[type='checkbox']:checked");
        await Assertions.Expect(checkedInputs).ToHaveCountAsync(3,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    }

    [Test]
    public async Task manage_modal_renders_six_manga_columns_no_sonarr_tv_columns()
    {
        // gh-226 absorption — the deleted ManageImportListsModalContent.test.tsx
        // Jest fixture asserted the COLUMNS array shape (6 manga columns; 5
        // forbidden Sonarr TV columns absent). The live equivalent inspects
        // the rendered <thead> on a real boot: each manga column header must
        // be present by its translated label text, and no Sonarr-only column
        // label (Protocol, RSS, Search, etc.) may render.
        //
        // Manga COLUMNS per ManageImportListsModalContent.tsx:54-92:
        //   - Name                  (translate('Name'))
        //   - Implementation        (translate('Implementation'))
        //   - Automatic Add         (translate('AutomaticAdd'))
        //   - Root Folder           (translate('RootFolder'))
        //   - Translation Profile   (translate('TranslationProfile'))
        //   - Tags                  (translate('Tags'))
        //
        // Forbidden Sonarr columns (Phase 27.1 D-03 substitution):
        //   - Protocol, Enable RSS, Enable Automatic Search,
        //     Enable Interactive Search, Priority
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(page.ManageButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await page.ManageButton.ClickAsync();

        var manageContent = Page.GetByTestId("manage-importlists-modal-content");
        await Assertions.Expect(manageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Wait for at least one row to be visible — confirms the table
        // rendered fully (the empty-data path skips the <Table> entirely so
        // there'd be no <thead> to inspect).
        var firstRow = manageContent.Locator("tbody tr").First;
        await Assertions.Expect(firstRow).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Manga column headers must each be present. Use a header-scoped
        // locator so any sibling table on the page can't satisfy the assertion.
        var headers = manageContent.Locator("thead th");

        // Exact-count guard (gh-226 PR #236 review — CodeRabbit Minor): the
        // COLUMNS array in ManageImportListsModalContent.tsx must yield
        // exactly 6 manga columns + 1 TableSelectCell header = 7 total <th>
        // elements. If a future plan adds an 8th column (or re-introduces a
        // Sonarr-TV column slipping past the forbidden-label checks below),
        // this assertion catches the drift before the label-text checks
        // even run.
        await Assertions.Expect(headers).ToHaveCountAsync(7,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

        var headerText = await headers.AllTextContentsAsync();
        var headerJoined = string.Join("|", headerText);

        headerJoined.Should().Contain("Name", "Name column must be present");
        headerJoined.Should().Contain("Implementation",
            "Implementation column must be present");
        headerJoined.Should().Contain("Automatic Add",
            "Automatic Add (enableAutomaticAdd) column must be present");
        headerJoined.Should().Contain("Root Folder",
            "Root Folder column must be present (manga rootFolderPath)");
        headerJoined.Should().Contain("Translation Profile",
            "Translation Profile column must be present (replaces TV quality-profile)");
        headerJoined.Should().Contain("Tags", "Tags column must be present");

        // Forbidden Sonarr TV columns: none may render in any header cell.
        // We assert exact-text equality (not Contain) per header so partial
        // overlaps (e.g. "Implementation" containing "men") don't false-trip.
        foreach (var th in headerText)
        {
            var trimmed = th.Trim();
            trimmed.Should().NotBe("Protocol",
                "Phase 27.1 D-03 forbids the Sonarr Protocol column on the Manage manga modal");
            trimmed.Should().NotBe("Enable RSS",
                "Phase 27.1 D-03 forbids the Sonarr Enable RSS column on the Manage manga modal");
            trimmed.Should().NotBe("Enable Automatic Search",
                "Phase 27.1 D-03 forbids the Sonarr Enable Automatic Search column");
            trimmed.Should().NotBe("Enable Interactive Search",
                "Phase 27.1 D-03 forbids the Sonarr Enable Interactive Search column");
            trimmed.Should().NotBe("Priority",
                "Phase 27.1 D-03 forbids the Sonarr Priority column on the Manage manga modal");
        }
    }

    [Test]
    public async Task manage_modal_bulk_delete_round_trip_only_deletes_selected_rows()
    {
        // gh-226 PR-review follow-up — the deleted Jest fixture
        // (ManageImportListsModalContent.test.tsx) asserted that the
        // ConfirmModal's onConfirmDelete callback dispatches
        // bulkDeleteImportLists({ ids: getSelectedIds() }) — the load-bearing
        // payload shape for DELETE /api/v5/importlist/bulk. The static-mock
        // assertion proved nothing about the real FE → BE wiring.
        //
        // This live test seeds 3 fresh rows independent of the [OneTimeSetUp]
        // baseline, opens the Manage modal, selects 2 of the 3, clicks Delete,
        // confirms in the ConfirmModal, and asserts via the V5 API that only
        // the unselected row survives. End-to-end proof that
        //   FE selection → onConfirmDelete → useBulkDeleteImportLists →
        //   DELETE /api/v5/importlist/bulk → ImportListController.DeleteBulk
        // wires the { ids } payload through correctly.
        //
        // Independent of [OneTimeSetUp] seeds because deleting fixture-level
        // rows would break sibling test methods.
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var kit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);

        var runTag = Guid.NewGuid().ToString("N").Substring(0, 6);
        var nameBulkA = $"BulkDel-A [{runTag}]";
        var nameBulkB = $"BulkDel-B [{runTag}]";
        var nameBulkC = $"BulkDel-C [{runTag}]";

        var (_, defBulkA) = await kit.RegisterMangaDexImportListAsync(nameBulkA);
        var (_, defBulkB) = await kit.RegisterMangaDexImportListAsync(nameBulkB);
        var (_, defBulkC) = await kit.RegisterMangaDexImportListAsync(nameBulkC);

        defBulkA.Should().NotBeNull();
        defBulkB.Should().NotBeNull();
        defBulkC.Should().NotBeNull();

        var idBulkA = defBulkA!.Value;
        var idBulkB = defBulkB!.Value;
        var idBulkC = defBulkC!.Value;

        try
        {
            var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(page.ManageButton).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            await page.ManageButton.ClickAsync();

            var manageContent = Page.GetByTestId("manage-importlists-modal-content");
            await Assertions.Expect(manageContent).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // Wait for all three seeded rows to be visible — proves the table
            // hydrated before we start clicking.
            var rowBulkA = manageContent.GetByTestId($"settings-importlist-row-{idBulkA}");
            var rowBulkB = manageContent.GetByTestId($"settings-importlist-row-{idBulkB}");
            var rowBulkC = manageContent.GetByTestId($"settings-importlist-row-{idBulkC}");
            await Assertions.Expect(rowBulkA).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            await Assertions.Expect(rowBulkB).ToBeVisibleAsync();
            await Assertions.Expect(rowBulkC).ToBeVisibleAsync();

            // Select rows A + B (leave C intact).
            await manageContent
                .GetByTestId($"settings-importlist-row-{idBulkA}-checkbox")
                .ClickAsync();
            await manageContent
                .GetByTestId($"settings-importlist-row-{idBulkB}-checkbox")
                .ClickAsync();

            // The Delete button lives in the modal footer's leftButtons block;
            // scope by manageContent so the global toolbar Delete (if any)
            // can't satisfy the locator. SpinnerButton renders no testid, so
            // we locate by visible text.
            var deleteFooterButton = manageContent
                .Locator("button")
                .Filter(new() { HasText = "Delete" })
                .First;
            await Assertions.Expect(deleteFooterButton).ToBeEnabledAsync(
                new LocatorAssertionsToBeEnabledOptions { Timeout = 10_000 });
            await deleteFooterButton.ClickAsync();

            // The ConfirmModal mounts as a separate portal-root modal with
            // role=dialog and a Delete button. Title is translated from the
            // i18n key 'DeleteSelectedImportLists' — live-verified to render
            // as "Delete Import List(s)" (not the verbatim key); filter on
            // that exact rendered string so the locator scopes to the
            // confirm dialog and not the underlying Manage modal.
            var confirmDialog = Page.GetByRole(AriaRole.Dialog)
                .Filter(new() { HasText = "Delete Import List(s)" })
                .First;
            await Assertions.Expect(confirmDialog).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            var confirmDeleteButton = confirmDialog
                .GetByRole(AriaRole.Button, new() { Name = "Delete" })
                .First;
            await confirmDeleteButton.ClickAsync();

            // Poll the V5 API until the two rows are gone. Proves the bulk
            // DELETE round-trip + the deletedIds-driven cache-update branch
            // in useBulkDeleteImportLists.onSuccess actually removes the
            // rows from the backend (not just from the FE cache).
            var deadline = DateTime.UtcNow.AddSeconds(15);
            int[] remainingTargetIds;
            do
            {
                var listResp = await http.GetAsync("importlist");
                listResp.IsSuccessStatusCode.Should().BeTrue(
                    "GET /api/v5/importlist must return 2xx after bulk-delete");
                var listBody = await listResp.Content.ReadAsStringAsync();
                using var listDoc = JsonDocument.Parse(listBody);
                var presentIds = listDoc.RootElement.EnumerateArray()
                    .Select(el => el.GetProperty("id").GetInt32())
                    .ToHashSet();
                remainingTargetIds = new[] { idBulkA, idBulkB, idBulkC }
                    .Where(presentIds.Contains)
                    .ToArray();
                if (remainingTargetIds.Length == 1 && remainingTargetIds[0] == idBulkC)
                {
                    break;
                }

                await Task.Delay(250);
            }
            while (DateTime.UtcNow < deadline);

            var survivingDescription = string.Join(", ", remainingTargetIds);
            var assertionMessage =
                $"after bulk-delete dispatched with {{ ids: [{idBulkA}, {idBulkB}] }}, only "
                + $"the unselected row (id={idBulkC}) should remain. Surviving ids of "
                + $"our 3-row test cohort: [{survivingDescription}]";

            remainingTargetIds.Should().BeEquivalentTo(new[] { idBulkC }, assertionMessage);
        }
        finally
        {
            // Defensive sweep — make the test idempotent even if the assertion
            // failed mid-flight (some rows may still exist; DELETE on a missing
            // id is harmless).
            _ = await http.DeleteAsync($"importlist/{idBulkA}");
            _ = await http.DeleteAsync($"importlist/{idBulkB}");
            _ = await http.DeleteAsync($"importlist/{idBulkC}");
        }
    }
}
