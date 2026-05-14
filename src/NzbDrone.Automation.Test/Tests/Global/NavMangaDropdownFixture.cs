using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ nav-manga sidebar anchor
/// (modal-action row 200 in INVENTORY.md). Mangarr's "top-nav" is actually
/// a left sidebar (PageSidebar.tsx); the row is named "Top-nav Manga dropdown"
/// in INVENTORY.md to preserve the Sonarr-shape naming convention. Each nav
/// fixture asserts the anchor renders with the expected href, plus the
/// sub-link list when it's the active parent.
///
/// State assertion: ToHaveAttributeAsync(href, regex) on the nav anchor.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavMangaDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion: nav-manga sidebar anchor present with `href="/"`
        // (PageSidebar.tsx L65-L77 — Manga is the root-route sidebar entry).
        var navManga = Page.GetByTestId("nav-manga");
        await Assertions.Expect(navManga).ToHaveAttributeAsync("href", new Regex(@"/$"));

        // STATE assertion 2: the child AddNew link is rendered when Manga is
        // the active parent (the root route makes it active). PageSidebar.tsx
        // L546-L560 renders children only when `link.to === activeParent`.
        var addNewChild = Page.Locator("a[href$='/add/manga']");
        await addNewChild.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        await Assertions.Expect(addNewChild.First).ToHaveAttributeAsync("href", new Regex(@"/add/manga$"));
    }
}
