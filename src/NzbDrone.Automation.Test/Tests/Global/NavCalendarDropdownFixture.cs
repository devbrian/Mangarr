using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ nav-calendar sidebar anchor
/// (modal-action row 201 in INVENTORY.md). Calendar is a leaf nav entry
/// (no children), so the assertion is just the anchor + href.
///
/// State assertion: ToHaveAttributeAsync(href, regex) on the nav anchor.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavCalendarDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion: nav-calendar sidebar anchor present with href="/calendar"
        // (PageSidebar.tsx L79-L84 — Calendar is a leaf entry, no children).
        var navCalendar = Page.GetByTestId("nav-calendar");
        await Assertions.Expect(navCalendar).ToHaveAttributeAsync("href", new Regex(@"/calendar$"));
    }
}
