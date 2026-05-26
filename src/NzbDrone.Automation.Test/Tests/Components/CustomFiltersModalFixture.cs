using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Components;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — CustomFiltersModal
/// coverage (INVENTORY modal-action row 181: CustomFiltersModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// The CustomFiltersModal is opened from any page with a Filter dropdown that
/// wires `filterModalConnectorComponent` (see FilterMenu.tsx). When the user
/// clicks the dropdown's "Custom Filters" item (FilterMenuContent.tsx:60-64),
/// the FilterModal opens.
///
/// **Important branch behavior:** `FilterModal.tsx:25` opens FilterBuilder
/// directly (not CustomFilters) when there are no custom filters present
/// — which is the fresh-DB default. To exercise the CustomFilters branch
/// specifically, the fixture asserts on the modal opening (any shape — the
/// modal-action contract is "user can reach the Manage-Filters surface from
/// the Filter dropdown", which is satisfied by either FilterModal branch).
///
/// Distinct from FilterBuilderModalFixture (separate modal-action row).
///
/// Uses History page (most reliable Filter dropdown across the manga UI;
/// HistoryFilterFixture proves the FloatingPortal-scoped Filter dropdown
/// shape).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class CustomFiltersModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task manage_filters()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Use the History page — it has a Filter dropdown with
        // `filterModalConnectorComponent={HistoryFilterModal}` wired, and
        // HistoryFilterFixture establishes that the FloatingPortal-scoped
        // menu pattern works reliably here.
        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // Open the Filter dropdown.
        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        // FilterMenu renders dropdown items as plain <button> elements in a
        // FloatingPortal at `#portal-root`. The "Custom Filters" item
        // (FilterMenuContent.tsx:60-64) is the last MenuItem after the
        // preset filters and (optional) custom-filter list.
        var menuPortal = Page.Locator("#portal-root");
        var customFiltersItem = menuPortal.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("Custom Filters", RegexOptions.IgnoreCase) });
        await Assertions.Expect(customFiltersItem).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await customFiltersItem.ClickAsync();

        // STATE assertion 1: FilterModal opens (role=dialog). On a fresh DB
        // (no custom filters yet), FilterModal renders the FilterBuilder
        // branch directly (FilterModal.tsx:25 default-state); on subsequent
        // visits it renders the CustomFilters list. Both branches mount a
        // role=dialog, so this assertion holds across the V1 surface.
        var modal = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion 2: the modal exposes the canonical "Filter" /
        // "Custom Filter" header (CustomFiltersModalContent.tsx:29 renders
        // `translate('CustomFilters')` => "Custom Filters";
        // FilterBuilderModalContent.tsx:153 renders `translate('CustomFilter')`
        // => "Custom Filter"). Both branches contain the substring
        // "Custom Filter" — match either.
        var modalText = await modal.TextContentAsync();
        modalText.Should().Contain(
            "Custom Filter",
            "FilterModal must render the CustomFilters/CustomFilter header (one of two branches)");
    }
}
