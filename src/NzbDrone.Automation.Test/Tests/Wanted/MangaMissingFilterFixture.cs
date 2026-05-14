using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

/// <summary>
/// Phase 18 Plan 18-18 — Missing filter coverage (INVENTORY req row 61,
/// WANTED-03: Wanted list filters by Manga + language + age rating).
///
/// Seeds a manga via AddMangaFlow, navigates to /manga/wanted/missing,
/// opens the Filter dropdown, applies a preset filter, and asserts the
/// filter state transitioned (the dropdown closes after selection and the
/// URL stays on the missing page).
///
/// State assertion: the filter dropdown opens (preset filter buttons
/// present inside the floating menu portal), a menu-item click triggers
/// the dropdown close (state-transition signal — the portal's filter
/// buttons disappear), and the URL stays on the missing page. Empty-row
/// state under fresh-DB is valid coverage — the contract here is filter
/// dropdown reachability + state-transition signal, not row-count change
/// (row-count change requires a populated missing list which lands in a
/// later plan).
///
/// Phase 19 Cat C triage: the Mangarr Menu component (Components/Menu/Menu.tsx)
/// renders dropdown items as plain &lt;button&gt; elements via MenuItem → Link;
/// no product code emits role="menuitem". The original fixture queried
/// AriaRole.Menuitem and always found 0 items — a fixture-logic bug, not a
/// product bug. Corrected to scope AriaRole.Button to the FloatingPortal
/// (#portal-root) that Menu renders the open dropdown into.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaMissingFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task missing_filter_menu_opens_and_applies_filter()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaMissingPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-missing-page")).ToBeVisibleAsync();

        // Missing toolbar has a FilterMenu wrapping MissingFilterModal
        // (Missing.tsx line 308-315). Click the Filter toolbar button.
        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        // FilterMenu renders its open dropdown into a FloatingPortal with
        // id="portal-root" (Components/Menu/Menu.tsx L156). Each preset
        // filter is a plain <button> there (MenuItem → Link → <button>;
        // no role="menuitem" is emitted by Mangarr's Menu component).
        // Scope the button query to the portal so the toolbar's own Filter
        // button is excluded.
        var menuPortal = Page.Locator("#portal-root");
        var menuItems = menuPortal.GetByRole(AriaRole.Button);

        // STATE assertion 2: dropdown actually opens with preset filters.
        // Missing.tsx wires FILTERS = Monitored / Unmonitored / Exclude
        // Specials plus a Custom Filters item — at least one must render.
        await menuItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var menuItemCount = await menuItems.CountAsync();
        menuItemCount.Should().BeGreaterThan(0, "filter dropdown must render at least one preset filter button");

        // Click the first preset filter (the "Monitored" preset). The click
        // triggers handleFilterSelect → setMissingOption('selectedFilterKey'),
        // which is the v5-state-transition signal.
        var firstMenuItem = menuItems.First;
        var menuItemText = await firstMenuItem.TextContentAsync();
        await firstMenuItem.ClickAsync();

        // STATE assertion 3: the dropdown actually closed — the floating
        // portal's filter buttons are detached after a preset selection.
        // This is the real state-transition signal (a no-op click that
        // failed to register would leave the dropdown open).
        await Assertions.Expect(menuItems.First).ToBeHiddenAsync(new()
        {
            Timeout = 10_000
        });

        // STATE assertion 4: URL preserved (filter selection transitioned
        // in-place — no spurious nav).
        Page.Url.Should().EndWith("/manga/wanted/missing");
        menuItemText.Should().NotBeNullOrWhiteSpace("filter dropdown item must render text content");
    }
}
