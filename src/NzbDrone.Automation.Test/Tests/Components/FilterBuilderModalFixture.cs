using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Components;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — FilterBuilderModal
/// coverage (INVENTORY modal-action row 182: FilterBuilderModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// FilterBuilderModalContent is rendered by FilterModal in either of two
/// branches:
///   (a) The customFilters list is empty (FilterModal.tsx:25 default-state)
///       — clicking "Custom Filters" in the dropdown opens FilterBuilder
///       directly.
///   (b) The user has saved custom filters and clicks the "+" / Add button
///       inside the CustomFiltersModalContent list.
///
/// On a fresh DB (no custom filters), the first click on "Custom Filters"
/// in the Filter dropdown opens FilterBuilder directly — that's branch (a),
/// the deterministic path this fixture exercises.
///
/// The Builder renders a `FilterBuilderRow` with a `key` (field-name) select
/// + `value` input + Add / Remove row buttons; the wire-shape contract for
/// FilterBuilder is "user can select a criterion field and define a value".
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class FilterBuilderModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task builder_works()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Use the History page (same reliable Filter dropdown surface as the
        // CustomFiltersModal sibling fixture).
        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        var menuPortal = Page.Locator("#portal-root");
        var customFiltersItem = menuPortal.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("Custom Filters", RegexOptions.IgnoreCase) });
        await Assertions.Expect(customFiltersItem).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await customFiltersItem.ClickAsync();

        // FilterModal opens. On the fresh-DB default (no custom filters),
        // FilterModal.tsx:25 renders FilterBuilderModalContent directly —
        // branch (a). The dialog renders the "Custom Filter" header
        // (FilterBuilderModalContent.tsx:153 — `translate('CustomFilter')`).
        var builder = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(builder).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion 1: the builder header renders "Custom Filter"
        // (FilterBuilderModalContent header — NOT "Custom Filters" plural which
        // is the CustomFiltersModalContent variant).
        var headerText = await builder.TextContentAsync();
        headerText.Should().Contain(
            "Custom Filter",
            "FilterBuilderModal must render the 'Custom Filter' header");

        // STATE assertion 2: the builder exposes the canonical Label input
        // (FilterBuilderModalContent.tsx:160-166 — the `name="label"` TextInput).
        // This is the load-bearing field that drives `saveCustomFilter`'s Label
        // arg; if the FilterBuilder doesn't render it, the user cannot save.
        var labelInput = builder.Locator("input[name='label']");
        await Assertions.Expect(labelInput).ToBeVisibleAsync();

        // STATE assertion 3: the builder mounts at least one FilterBuilderRow
        // (FilterBuilderModalContent.tsx:172-189 — the `filters.map(...)`
        // block). The row carries a field-key selector (SelectInput or
        // role="combobox"). The default-state filters[] is `[NEW_FILTER]`
        // (FilterBuilderModalContent.tsx:77) so one row mounts immediately.
        var selectors = builder.Locator("select, [role='combobox']");
        var selectorCount = await selectors.CountAsync();
        selectorCount.Should().BeGreaterThan(
            0,
            "FilterBuilder must render at least one FilterBuilderRow with a field-key selector");

        // STATE assertion 4: the builder exposes the canonical "Save" button
        // (FilterBuilderModalContent.tsx:196-202 — SpinnerErrorButton). Without
        // a Save button, the filter cannot persist — the modal would be a
        // dead-end. The button's accessible name is the literal "Save"
        // (`translate('Save')`).
        var saveButton = builder.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" });
        await Assertions.Expect(saveButton).ToBeVisibleAsync();
    }
}
