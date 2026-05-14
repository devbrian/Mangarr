using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ nav-wanted sidebar entry
/// (modal-action row 203 in INVENTORY.md). Wanted has 2 children
/// (Missing / CutoffUnmet); they mount when /manga/wanted/* is active.
///
/// State assertion: ToHaveAttributeAsync(href) on parent + .CountAsync() on
/// the two expected child anchors.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavWantedDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        // Navigate to a Wanted sub-route so Wanted is the active parent and
        // its children render (PageSidebar.tsx L546 gating).
        await Page.GotoAsync($"{RootUri}/manga/wanted/missing");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: parent nav-wanted anchor (PageSidebar.tsx L108-L123).
        var navWanted = Page.GetByTestId("nav-wanted");
        await Assertions.Expect(navWanted).ToHaveAttributeAsync("href", new Regex(@"/manga/wanted/missing$"));

        // STATE assertion 2: both child links present (Missing / CutoffUnmet).
        var missingChild = Page.Locator("a[href$='/manga/wanted/missing']");
        var cutoffChild = Page.Locator("a[href$='/manga/wanted/cutoffunmet']");

        (await missingChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await cutoffChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
    }
}
