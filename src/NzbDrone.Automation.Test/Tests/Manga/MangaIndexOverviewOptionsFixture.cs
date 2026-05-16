using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `MangaIndexOverviewOptionsModal` (MangaIndex Overview Options).
///
/// Default view is 'posters' (mangaOptionsStore.ts:73 Lock #2); flip to
/// 'overview' via the ViewMenu so the Options toolbar button mounts
/// MangaIndexOverviewOptionsModalContent.tsx (header "Overview Options").
/// The form persists changes through useMangaOverviewOptions into the
/// 'manga_options' localStorage key.
///
/// Flow: AddMangaFlow seed → switch view to Overview (ViewMenu) → click
/// Options → toggle "Show Monitored" → close → reload (restore view +
/// localStorage) → reopen → assert the checkbox state flipped vs. initial.
///
/// State assertion: toggle state PERSISTED across hard Page.ReloadAsync.
///
/// Blocker #4: 1 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexOverviewOptionsFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task options_persist()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();
        await Page.Locator("[data-testid^='manga-card-']").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });

        // Switch to Overview view via the ViewMenu (MangaIndexViewMenu.tsx renders
        // ViewMenuItem entries "Table" / "Posters" / "Overview").
        await SwitchToOverviewAsync();

        // Open the Overview Options modal.
        var optionsButton = Page.GetByRole(AriaRole.Button, new() { Name = "Options" }).First;
        await Assertions.Expect(optionsButton).ToBeVisibleAsync();
        await optionsButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Overview Options" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var showMonitoredInput = modal.Locator("input[name='showMonitored']").First;
        var initialState = await showMonitoredInput.IsCheckedAsync();
        var labelForShowMonitored = modal.Locator("label:has(input[name='showMonitored'])").First;
        await labelForShowMonitored.ClickAsync();

        await Assertions.Expect(showMonitoredInput).ToBeCheckedAsync(
            new LocatorAssertionsToBeCheckedOptions { Checked = !initialState });

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync();

        // Hard reload — localStorage survives 'manga_options' (incl. view='overview').
        await Page.ReloadAsync();

        // debug-30 iter-2 (2026-05-16): manga-card-* testid is ONLY rendered
        // by MangaIndexPoster (Posters view). After reload the view is
        // restored from localStorage as 'overview', so MangaIndexOverviews
        // mounts — no manga-card-* exists. Wait for the page-shell testid
        // + the Options button (which is what we click next anyway).
        await Page.GetByTestId("manga-index-page").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });

        var optionsButton2 = Page.GetByRole(AriaRole.Button, new() { Name = "Options" }).First;
        await Assertions.Expect(optionsButton2).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await optionsButton2.ClickAsync();

        var modal2 = Page.GetByRole(AriaRole.Dialog, new() { Name = "Overview Options" });
        await Assertions.Expect(modal2).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var showMonitoredInput2 = modal2.Locator("input[name='showMonitored']").First;
        var newState = await showMonitoredInput2.IsCheckedAsync();
        newState.Should().Be(!initialState,
            "MangaIndexOverviewOptionsModal must persist Show Monitored toggle across reload");
    }

    private async Task SwitchToOverviewAsync()
    {
        // ViewMenu trigger is a ToolbarMenuButton rendered with text "View"
        // (translate('View')) per ViewMenu.tsx. Click it to expand the menu,
        // then click the "Overview" ViewMenuItem.
        var viewMenuTrigger = Page.GetByRole(AriaRole.Button, new() { Name = "View" }).First;
        await viewMenuTrigger.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await viewMenuTrigger.ClickAsync();

        // debug-30 (2026-05-16): MenuItem renders as <Link>→<button> (role=Button,
        // NOT Menuitem) per MenuItem.tsx. Scope by exact-name "Overview" so we
        // don't collide with the "Overview Options" modal Options button.
        var overviewItem = Page.GetByRole(AriaRole.Button, new() { Name = "Overview", Exact = true });
        await Assertions.Expect(overviewItem).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await overviewItem.ClickAsync();

        // Wait for the overview-specific markers to mount. The Options button
        // re-renders with the OVERVIEW icon — the click is the state-transition
        // gate, so a short settle is sufficient.
        await Page.WaitForTimeoutAsync(500);
    }
}
