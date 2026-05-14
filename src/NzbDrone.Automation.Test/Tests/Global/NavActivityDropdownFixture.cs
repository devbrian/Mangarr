using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ nav-activity sidebar entry
/// (modal-action row 202 in INVENTORY.md). Activity has 3 children
/// (Queue/History/Blocklist); they render under the parent when it's the
/// active parent. We navigate to /manga/activity/queue so Activity becomes
/// the active parent and the children mount.
///
/// State assertion: ToHaveAttributeAsync(href) on parent + child counts via
/// .Should().BeGreaterOrEqualTo for the three expected child hrefs.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavActivityDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        // Navigate to an Activity sub-route so Activity is the active parent
        // and its children render (PageSidebar.tsx L546 gates children behind
        // `link.to === activeParent`).
        await Page.GotoAsync($"{RootUri}/manga/activity/queue");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: parent nav-activity anchor (PageSidebar.tsx L86-L106).
        var navActivity = Page.GetByTestId("nav-activity");
        await Assertions.Expect(navActivity).ToHaveAttributeAsync("href", new Regex(@"/manga/activity/queue$"));

        // STATE assertion 2: three child links present (Queue/History/Blocklist).
        // PageSidebar.tsx renders child links as <a> with the child.to as href.
        // Use locator counts as the STATE check — each must resolve to ≥1 anchor.
        var queueChild = Page.Locator("a[href$='/manga/activity/queue']");
        var historyChild = Page.Locator("a[href$='/manga/activity/history']");
        var blocklistChild = Page.Locator("a[href$='/manga/activity/blocklist']");

        (await queueChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await historyChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await blocklistChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
    }
}
