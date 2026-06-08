using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `DeleteMangaSelectModal` (MangaIndex bulk-select Delete).
///
/// Adapts the MangaIndexBulkActionsFixture canonical shape — 2-manga seed via
/// AddMangaFlow → select-mode → Select All → bulk Delete → confirm → assert the
/// card count transitions 2 → 0 (DELETE /api/v5/manga/editor contract). Splits
/// the prior MangaIndexBulkActionsFixture's responsibility so the modal-action
/// row has a 1:1 fixture mapping per Phase 20 Wave-3 inventory-greening goal.
///
/// Blocker #4 compliance: 2 manga seeded upfront via AddMangaFlow so select-mode +
/// bulk-action assertions are deterministic; no inconclusive-skip branches.
/// Pitfall 10 compliance: Comix indexer disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkDeleteFixture : AutomationTest
{
    private const string KnownMangaBakaId  = AddMangaFlow.KnownMangaBakaId;
    private const string KnownMangaBakaId2 = AddMangaFlow.KnownMangaBakaId2;

    [Test]
    public async Task bulk_delete()
    {
        // Seed 2 manga (guarantees select-mode has rows; Blocker #4 — no Inconclusive).
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();
        await Assertions.Expect(index.Grid).ToBeVisibleAsync();

        // STATE assertion 1 (pre): 2 cards present (the AddMangaFlow seed contract).
        var allCardsBefore = Page.Locator("[data-testid^='manga-card-']");
        var countBefore = await allCardsBefore.CountAsync();
        countBefore.Should().Be(2, "two manga seeded via AddMangaFlow");

        // Enter select mode then select all.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        // Click the bulk Delete button in the MangaIndexSelectFooter.
        var deleteButton = Page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).First;
        await deleteButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await deleteButton.ClickAsync();

        // Modal opens — DeleteSelectedManga header per DeleteMangaModalContent.tsx.
        var deleteModal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Delete Selected Manga" });
        await Assertions.Expect(deleteModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Confirm delete (scoped to the dialog so it doesn't race the footer).
        var confirmButton = deleteModal.GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Delete", Exact = true });
        await confirmButton.ClickAsync();

        // Wait for the first card to detach (the DELETE /api/v5/manga/editor RTT).
        var allCardsAfter = Page.Locator("[data-testid^='manga-card-']");
        await allCardsAfter.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });

        // STATE assertion 2: card count goes 2 → 0 after the DELETE.
        var countAfter = await allCardsAfter.CountAsync();
        countAfter.Should().Be(0, "bulk delete must remove both manga from the index grid");
    }
}
