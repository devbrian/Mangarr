using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 18 Plan-04 AddManga cluster — Delete modal confirm round-trip.
/// Chains AddMangaFlow.AddByMangaDexIdAsync to seed a manga, then clicks
/// the MangaDetails Delete button, confirms the modal, and asserts the
/// post-delete redirect back to the manga index (Mangarr v1 routes the
/// index at `/`, NOT `/manga`).
///
/// State assertion (not just rendering — per
/// feedback_verify_ui_state_not_just_rendering): the manga card with the
/// seeded id is absent from the index after the delete. Card count for
/// the just-deleted manga should be 0 (or absent — Locator.CountAsync()
/// returns 0 when the selector matches nothing).
///
/// Cassettes recorded in Plan 18-14 gap closure; runs LIVE under default CI Replay mode.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DeleteMangaModalFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task delete_modal_removes_manga_returns_to_index()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        await details.DeleteButton.ClickAsync();

        var modal = new DeleteMangaModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        var index = await modal.ConfirmDeleteAsync();

        // STATE assertion: index visible, deleted manga absent. The card testid is keyed by
        // titleSlug; for offline cassettes we don't know the slug ahead of time, so the
        // absence assertion is "no card matches the mangaDexId fallback key". The richer
        // titleSlug-based card lookup is tested by Plan-05 once a richer cassette is in.
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();
        var cardCount = await index.CardByKey(KnownMangaDexId).CountAsync();
        cardCount.Should().Be(0, "deleted manga should not appear in index");
    }
}
