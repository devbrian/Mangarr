using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `MangaIndexPosterOptionsModal` (MangaIndex Posters Options).
///
/// Default view is 'posters' (mangaOptionsStore.ts:73 Lock #2). The Options
/// toolbar button mounts MangaIndexPosterOptionsModalContent.tsx — header
/// "Poster Options". The form persists changes through the zustand
/// useMangaPosterOptions hook into the 'manga_options' localStorage key, so a
/// reload must surface the toggled state.
///
/// Flow: AddMangaFlow seed (Options button gated `isDisabled={hasNoManga}`) →
/// click Options → toggle "Show Title" → close → reload → reopen → assert the
/// checkbox state flipped vs. the initial default (true → false).
///
/// State assertion: the toggle state PERSISTED across a hard Page.ReloadAsync
/// (zustand → localStorage round-trip).
///
/// Blocker #4: 1 manga seeded upfront; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexPosterOptionsFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task options_persist()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        // Wait for the manga card so hasNoManga flips false and Options enables.
        await Page.Locator("[data-testid^='manga-card-']").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });

        // Open the Posters Options modal (default view is 'posters').
        var optionsButton = Page.GetByRole(AriaRole.Button, new() { Name = "Options" }).First;
        await Assertions.Expect(optionsButton).ToBeVisibleAsync();
        await optionsButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Poster Options" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The Show Title CheckInput hidden <input> sits behind a styled overlay.
        // Click the wrapping label to toggle (same approach as
        // BlocklistBulkRemoveFixture's selectAll click).
        var showTitleInput = modal.Locator("input[name='showTitle']").First;
        var initialState = await showTitleInput.IsCheckedAsync();
        var labelForShowTitle = modal.Locator("label:has(input[name='showTitle'])").First;
        await labelForShowTitle.ClickAsync();

        // Wait for the bound state to actually flip before reloading
        // (zustand's set is sync but the React commit + localStorage write
        // chain needs a re-render tick).
        await Assertions.Expect(showTitleInput).ToBeCheckedAsync(
            new LocatorAssertionsToBeCheckedOptions { Checked = !initialState });

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync();

        // Hard reload — localStorage survives, in-memory React state resets.
        await Page.ReloadAsync();
        await Page.Locator("[data-testid^='manga-card-']").First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 15_000 });

        var optionsButton2 = Page.GetByRole(AriaRole.Button, new() { Name = "Options" }).First;
        await optionsButton2.ClickAsync();

        var modal2 = Page.GetByRole(AriaRole.Dialog, new() { Name = "Poster Options" });
        await Assertions.Expect(modal2).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion: the toggled value persisted across the hard reload
        // (zustand → 'manga_options' localStorage key per mangaOptionsStore.ts:68).
        var showTitleInput2 = modal2.Locator("input[name='showTitle']").First;
        var newState = await showTitleInput2.IsCheckedAsync();
        newState.Should().Be(!initialState,
            "MangaIndexPosterOptionsModal must persist Show Title toggle across reload via 'manga_options' localStorage");
    }
}
