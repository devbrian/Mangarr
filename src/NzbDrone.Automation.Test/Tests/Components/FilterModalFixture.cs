using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Components;

// Phase 18 Plan 18-16 Task 2 — Tests/Components/ FilterModal — the generic
// Filter widget (modal-action row 180 in INVENTORY.md). The widget is
// Components/Menu/FilterMenu.tsx; it renders a ToolbarMenuButton with the
// translated "Filter" label and an attached MenuContent dropdown (portalled
// to document.body#portal-root via @floating-ui/react).
//
// Why /manga/wanted/missing: MangaIndex's FilterMenu is disabled when
// hasNoManga (MangaIndex.tsx L290 `isDisabled={hasNoManga}`). The Wanted
// Missing page (Missing.tsx L308) wires the FilterMenu without the
// isDisabled gate, so the generic widget contract is exercisable even on
// a fresh-DB baseline seed.
//
// State assertions:
// 1. The Filter button is in the DOM with the translated "Filter" label.
// 2. After clicking, the dropdown menu (rendered into the FloatingPortal
//    at id="portal-root") renders ≥1 filter option button. The seeded
//    Wanted/Missing filters include "All" and "Monitored Only" per
//    Missing.tsx FILTERS constant.
//
// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
//
// gh #152 (Class 3 — assertion count = 0) fix-forward: the prior fixture
// asserted on Page.GetByRole(AriaRole.Menuitem) — but FilterMenuItem →
// SelectedMenuItem → MenuItem → Link renders as a button (Link.tsx
// L91-104 — no `to` prop, falls through to the default button branch),
// NOT an element with role="menuitem". The Menuitem locator returned
// zero matches regardless of menu state. Switched to a portal-scoped
// button-role locator (the menu items are buttons inside
// #portal-root, the FloatingPortal mount point from Menu.tsx:156).
[TestFixture]
[Category("AutomationTest")]
public class FilterModalFixture : AutomationTest
{
    [Test]
    public async Task filter_widget_works()
    {
        // The Wanted/Missing page renders FilterMenu unconditionally
        // (Missing.tsx L308 has no isDisabled prop), so the widget responds
        // even with no manga seeded.
        await Page.GotoAsync($"{RootUri}/manga/wanted/missing");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: the Filter button is in the DOM. The widget
        // uses ToolbarMenuButton with text={translate('Filter')} → "Filter"
        // per en.json. Use GetByRole to locate the rendered button.
        var filterButton = Page.GetByRole(AriaRole.Button, new() { Name = "Filter" });
        await filterButton.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // Click to open the dropdown. @floating-ui/react portals the menu
        // content into #portal-root (Menu.tsx:156-157), so the rendered items
        // live OUTSIDE the page-content subtree but still on the document.
        await filterButton.First.ClickAsync();

        // STATE assertion 2: the portalled dropdown rendered ≥1 filter item.
        // MenuItem renders as a `<button>` (Link.tsx fallback), not a
        // role="menuitem" — locate by button role scoped to the portal mount.
        // The Wanted/Missing FILTERS list seeds at least "All" + "Monitored
        // Only" entries, so the count is deterministic regardless of library
        // contents.
        //
        // 2026-05-15 gh-152 verification fix: the wrapper `<div id="portal-root">`
        // is always Attached but Playwright reports it as Hidden when empty
        // (no width/height) — `WaitForAsync` with default `Visible` state times
        // out. Wait for menu items inside the portal directly; they are visible
        // once @floating-ui mounts the dropdown content.
        var portal = Page.Locator("#portal-root");
        var menuItems = portal.GetByRole(AriaRole.Button);
        await menuItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var count = await menuItems.CountAsync();
        count.Should().BeGreaterOrEqualTo(
            1,
            "because the FilterMenu dropdown must surface at least one filter option (e.g. All / Monitored / Unmonitored)");
    }
}
