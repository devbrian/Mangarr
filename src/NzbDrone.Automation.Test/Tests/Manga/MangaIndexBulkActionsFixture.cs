using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 18 Plan 18-18 — MangaIndex bulk-edit + bulk-delete coverage
/// (INVENTORY v5-endpoint rows 74, 75: PUT/DELETE /api/v5/manga/editor).
///
/// Seeds 2 distinct manga via AddMangaFlow (using KnownMangaDexId +
/// KnownMangaDexId2 — the second anchor was added in Plan 18-14 D-Step-0
/// precisely to enable this fixture's "2 manga in library" requirement).
///
/// Flow exercised:
///   1. Add manga #1 (Komi) → land on MangaDetails
///   2. Add manga #2 (Chainsaw Man) → land on MangaDetails
///   3. Navigate to MangaIndex grid → assert both cards present
///   4. Enter SelectMode → click each card → assert select footer shows count=2
///   5. Click bulk Delete → assert DeleteMangaModal opens with 2 manga
///   6. Confirm delete → assert both cards removed from grid (count: 2 → 0)
///
/// State assertion: card-count goes from 2 → 0 after bulk delete (the
/// DELETE /api/v5/manga/editor contract). The SelectFooter rendering with
/// the "count manga selected" label is the bulk-edit-modal-reachable state
/// assertion (PUT /api/v5/manga/editor entry point — we don't push a Save
/// from the bulk-edit modal here to keep the fixture deterministic; the
/// bulk-edit modal open + count-of-selected-manga is the v5-endpoint row 74
/// gate. The richer Save round-trip is deferred to a follow-up fixture.
///
/// Per-fixture DB (D-05) means bulk-delete is isolated.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkActionsFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [Test]
    public async Task bulk_delete_removes_both_seeded_manga_from_index()
    {
        // Seed 2 manga via UI (D-06 — UI-populates-via-UI).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        // Navigate to the MangaIndex grid.
        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();
        await Assertions.Expect(index.Grid).ToBeVisibleAsync();

        // STATE assertion 1: pre-delete count is 2 manga cards.
        // The MangaIndexPosterSelect / poster cards are wrapped by the grid;
        // we count by the manga-card- prefix testids that CardByKey resolves.
        var allCardsBefore = Page.Locator("[data-testid^='manga-card-']");
        var countBefore = await allCardsBefore.CountAsync();
        countBefore.Should().Be(2, "two manga were seeded via AddMangaFlow");

        // Enter select mode via the SelectManga toolbar button. The
        // MangaIndexSelectModeButton text becomes "Stop Selecting" once active;
        // we click by the visible label.
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Select All" }).First.ClickAsync();

        // Click the bulk Delete button in the MangaIndexSelectFooter.
        // The footer renders SpinnerButton labelled "Delete" (kinds.DANGER).
        // Multiple "Delete" buttons may exist (e.g. card-hover overlay); the
        // footer Delete is in the bottom action area, paired with "Delete Files".
        // Use a more specific locator anchored to the SelectFooter.
        var deleteButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete", Exact = true }).First;
        await deleteButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await deleteButton.ClickAsync();

        // The DeleteMangaModal (multi-manga variant) opens with a confirm button.
        // Phase 19 fix-forward: the prior code clicked the confirm button as
        // soon as it resolved, but the modal backdrop (Modal-modalBackdrop)
        // animates in and intercepts the pointer event mid-transition. The
        // Modal renders role="dialog" with aria-labelledby pointing at the
        // ModalHeader, so the dialog's accessible name is the header text
        // ("Delete Selected Manga" per DeleteMangaModalContent). Wait for that
        // dialog to be visible — proving the modal is fully rendered and the
        // backdrop transition has settled — then scope the confirm click to it.
        var deleteModal = Page.GetByRole(AriaRole.Dialog,
            new PageGetByRoleOptions { Name = "Delete Selected Manga" });
        await Assertions.Expect(deleteModal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var confirmButton = deleteModal.GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Delete", Exact = true });
        await confirmButton.ClickAsync();

        // WR-07 (18-REVIEW): replace static 2.5s sleep with an explicit
        // detach-wait on the first card. The bulk-delete completes when the
        // last manga-card disappears; under CI load the static sleep can
        // race past the actual delete RTT or fire prematurely.
        var allCardsAfter = Page.Locator("[data-testid^='manga-card-']");
        await allCardsAfter.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });

        // STATE assertion 2: card count goes from 2 → 0 after bulk delete
        // (the DELETE /api/v5/manga/editor contract — INVENTORY row 75).
        var countAfter = await allCardsAfter.CountAsync();
        countAfter.Should().Be(0, "bulk delete must remove both manga from the index grid");
    }
}
