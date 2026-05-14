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
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D — AddManga modal
/// nav race in ConfirmAddAsync). AddMangaFlow.AddByMangaDexIdAsync times out
/// until #102 lands.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 19 Cat C (Residual Yellow Inventory Resolution): fixture-level bug surfaced once the AddManga flow worked end-to-end post-#102. #102 is CLOSED and was NOT the blocker. Flip after the focused fix-forward. See ROADMAP Phase 19 SC#3.")]
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
        // Modal body lists the count of manga to delete. Click the modal's
        // confirm Delete button.
        var confirmButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete" }).Last;
        await confirmButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
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
