using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// GH #224 regression — Manage Indexers modal sort + shift-click range-select.
///
/// Pre-fix bug shape:
///   - <c>useSortedIndexers</c> hardcoded <c>sortByProp('name')</c> while the
///     table's column-header click dispatched into the Zustand
///     <c>useManageIndexersOptions</c> store (silently ignored). Column-header
///     clicks updated the Zustand state but the rendered table never re-sorted.
///   - Additionally, <c>&lt;SelectProvider items={useIndexersData()}&gt;</c>
///     received the unsorted raw cache while the inner <c>&lt;Table&gt;</c>
///     rendered sorted data. Shift-click range-select therefore toggled the
///     contiguous block of the UNSORTED array, which differs from what the user
///     saw on screen.
///
/// To make BOTH bugs detectable in a single fixture, we seed 3 rows in NON-
/// alphabetical INSERTION order (CCC, AAA, BBB) so:
///   - Unsorted (raw cache, by insertion / id-asc): [CCC, AAA, BBB]
///   - Visible (default sort = name ascending):     [AAA, BBB, CCC]
///
/// The two views genuinely disagree on contiguous-block membership.
///
/// Assertions (state-not-rendering — per
/// feedback_verify_ui_state_not_just_rendering.md):
///   1. Initial visible first-row name = "AAA Indexer" (proves the default
///      Zustand sort key 'name' + 'ascending' is what renders).
///   2. Click the Name column header to flip to descending. Visible first-row
///      name flips to "CCC Indexer". Pre-fix this would NOT happen (Zustand
///      flips but render does not). The <c>aria-sort</c> attribute on the Name
///      column header also flips to "descending".
///   3. Click the Name column header AGAIN to flip back to ascending. Visible
///      first row returns to "AAA Indexer".
///   4. Shift-click range-select: click row AAA's checkbox, shift-click row
///      CCC's checkbox. The visible range AAA→BBB→CCC has 3 rows. The
///      UNSORTED-array range from AAA (insertion-index 1) to CCC (insertion-
///      index 0) is [CCC, AAA] (2 rows) — pre-fix would only select 2 rows.
///      Post-fix the SelectProvider walks the SAME sorted array as the table,
///      so the range-select picks all 3 rows.
///
/// Mirrors the analog at ManageImportListsSortAndRangeSelectFixture.cs.
/// Tier: nightly (no PRSmoke).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ManageIndexersSortAndRangeSelectFixture : AutomationTest
{
    private const string Name_A = "AAA Indexer";
    private const string Name_B = "BBB Indexer";
    private const string Name_C = "CCC Indexer";

    private int _idC;
    private int _idA;
    private int _idB;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var kit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);

        // Debug session manage-indexers-sort-timeout (2026-05-22) — delete the
        // auto-seeded MangaDex + Comix indexer rows BEFORE seeding the fixture's
        // AAA/BBB/CCC trio. Pre-fix the fresh-DB
        // IndexerFactory.InitializeProviders seed left the table containing
        // [MangaDex, Comix] before fixture seed ran. After name-descending sort,
        // the first visible row was "MangaDex" (not "CCC Indexer"), so the
        // WaitForFunctionAsync at line 131 timed out. With the table empty
        // pre-seed, the fixture-seeded trio is the only data the Manage modal
        // renders and the descending-sort first row is deterministically
        // "CCC Indexer".
        //
        // DisableComixIndexerAsync became a no-op once the row is deleted, so
        // the explicit disable call is dropped (the pre-fix Pitfall 10 concern
        // was that the EditIndexerModal's picker would warm Comix's schema and
        // poke PuppeteerSharp; with the row gone the picker never reaches Comix).
        await kit.DeleteAllIndexersAsync();

        // Seed 3 MangaDex-implementation indexer rows in NON-alphabetical
        // insertion order (C, A, B) so the unsorted raw cache shape disagrees
        // with the visible (name-sorted) order. See class-level doc-comment for
        // why this matters.
        _idC = await kit.SeedIndexerAsync(Name_C);
        _idA = await kit.SeedIndexerAsync(Name_A);
        _idB = await kit.SeedIndexerAsync(Name_B);
    }

    [Test]
    public async Task column_header_click_resorts_visible_rows_and_shift_click_range_selects_visible_block()
    {
        var page = await new SettingsIndexersPage(Page).OpenAsync(RootUri);

        // 1. Open the Manage modal. The toolbar Manage button is the canonical
        //    entry point. After click the modal's `manage-indexers-modal-content`
        //    testid (added in the GH #224 fix-forward for parity with the
        //    ImportLists peer) is the assertable root.
        await Assertions.Expect(page.ManageButton).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await page.ManageButton.ClickAsync();

        var manageContent = Page.GetByTestId("manage-indexers-modal-content");
        await Assertions.Expect(manageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var rowA = manageContent.GetByTestId($"settings-indexer-row-{_idA}");
        var rowB = manageContent.GetByTestId($"settings-indexer-row-{_idB}");
        var rowC = manageContent.GetByTestId($"settings-indexer-row-{_idC}");

        await Assertions.Expect(rowA).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(rowB).ToBeVisibleAsync();
        await Assertions.Expect(rowC).ToBeVisibleAsync();

        // 2. Default sort is name-ascending. Assert visible first row = AAA.
        var firstRowNameSelector =
            "[data-testid='manage-indexers-modal-content'] tbody tr td:nth-child(2)";

        var firstRowName0 = await Page.Locator(firstRowNameSelector).First.TextContentAsync();
        firstRowName0.Should().Contain(Name_A,
            "default sortKey=name + sortDirection=ascending should put '{0}' on top",
            Name_A);

        // 3. Click the Name header to flip to descending.
        //
        //    Pre-fix bug: column-header click dispatched into the Zustand store
        //    but `useSortedIndexers` was bound to a hardcoded 'name' sort — so
        //    the table never re-rendered. Visible first row would stay 'AAA'.
        //
        //    Post-fix: `useSortedManageIndexers` reads the Zustand sortKey +
        //    sortDirection and the rendered first row flips to 'CCC'. Header
        //    carries data-testid="settings-indexer-name-header" plumbed through
        //    Column → Table → TableHeaderCell → Link; the testid contract per
        //    D-18 keeps this fixture decoupled from DOM structure.
        var nameHeader = Page.GetByTestId("settings-indexer-name-header");
        await nameHeader.ClickAsync();

        // Wait for re-render. Pre-fix this WaitForFunction would time out
        // (visible first-row never flips), making the fixture fail loudly.
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector(\"{firstRowNameSelector}\")?.textContent?.trim().startsWith('CCC')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // State assertion (per feedback_verify_ui_state_not_just_rendering):
        // the column header's aria-sort attribute flipped to descending.
        var nameAriaSortDesc = await nameHeader.GetAttributeAsync("aria-sort");
        nameAriaSortDesc.Should().Be("descending");

        // 4. Click Name again to flip back to ascending. Visible first row
        //    returns to 'AAA'. Re-asserts the bind is two-way (Zustand → render
        //    AND that the Zustand toggle dispatches correctly).
        await nameHeader.ClickAsync();
        await Page.WaitForFunctionAsync(
            $"() => document.querySelector(\"{firstRowNameSelector}\")?.textContent?.trim().startsWith('AAA')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        var nameAriaSortAsc = await nameHeader.GetAttributeAsync("aria-sort");
        nameAriaSortAsc.Should().Be("ascending");

        // 5. Shift-click range-select. Visible order is now [AAA, BBB, CCC].
        //    Click AAA's checkbox, then shift-click CCC's checkbox.
        //
        //    Pre-fix bug: <SelectProvider items={unsorted}> — unsorted is
        //    [CCC, AAA, BBB] in insertion order. `getToggledRange` walks that
        //    array. AAA sits at unsorted-index 1, CCC sits at unsorted-index 0.
        //    Range [0..1] = {CCC, AAA} = 2 rows. The user sees AAA, BBB, CCC
        //    highlighted but only AAA + CCC end up selected (BBB is missed).
        //
        //    Post-fix: <SelectProvider items={sortedSameAsTable}>. The visible
        //    range AAA→CCC walks the same array. {AAA, BBB, CCC} = 3 rows.
        //
        //    Load-bearing assertion: input[type='checkbox']:checked count == 3
        //    inside the modal body. Pre-fix this would be 2.
        var rowA_checkbox = manageContent.GetByTestId(
            $"settings-indexer-row-{_idA}-checkbox");
        var rowC_checkbox = manageContent.GetByTestId(
            $"settings-indexer-row-{_idC}-checkbox");

        await rowA_checkbox.ClickAsync();
        await rowC_checkbox.ClickAsync(new LocatorClickOptions { Modifiers = new[] { KeyboardModifier.Shift } });

        // The CheckInput renders <input type="checkbox" checked={isChecked}>
        // wrapped in a click-handler <label>. Count those inputs that are
        // checked inside the manage modal's tbody to assert exactly 3 rows
        // selected.
        var checkedInputs = Page.Locator(
            "[data-testid='manage-indexers-modal-content'] tbody input[type='checkbox']:checked");
        await Assertions.Expect(checkedInputs).ToHaveCountAsync(3,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    }
}
