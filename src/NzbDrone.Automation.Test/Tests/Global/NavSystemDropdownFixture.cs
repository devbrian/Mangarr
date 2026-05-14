using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Global/ nav-system sidebar entry
/// (modal-action row 205 in INVENTORY.md). System parent expands 6
/// children (Status / Tasks / Backup / Updates / Events / LogFiles) when
/// /system/* is active.
///
/// State assertion: ToHaveAttributeAsync(href) on parent + .CountAsync()
/// on a representative subset of expected child anchors.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavSystemDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        // System parent (PageSidebar.tsx L185-L217) `to` = "/system/status".
        await Page.GotoAsync($"{RootUri}/system/status");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: parent nav-system anchor.
        var navSystem = Page.GetByTestId("nav-system");
        await Assertions.Expect(navSystem).ToHaveAttributeAsync("href", new Regex(@"/system/status$"));

        // STATE assertion 2: representative child links present.
        var statusChild = Page.Locator("a[href$='/system/status']");
        var tasksChild = Page.Locator("a[href$='/system/tasks']");
        var backupChild = Page.Locator("a[href$='/system/backup']");

        (await statusChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await tasksChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await backupChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
    }
}
