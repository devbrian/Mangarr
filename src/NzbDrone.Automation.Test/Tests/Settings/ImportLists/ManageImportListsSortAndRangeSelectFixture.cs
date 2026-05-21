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

        await kit.DisableComixIndexerAsync();

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
}
