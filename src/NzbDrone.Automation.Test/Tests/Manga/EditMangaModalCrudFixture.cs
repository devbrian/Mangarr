using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 18 Plan 18-18 — EditManga CRUD round-trip (INVENTORY v5-endpoint row 72:
/// PUT /api/v5/manga/{id}).
///
/// Closes the gap left by Plan-04's EditMangaModalFixture (which asserts the
/// modal opens + auto-closes on save) by actually flipping a field, saving,
/// reopening the modal, and asserting the flipped value persisted. That second
/// open-and-read pass is the "state-not-rendering" gate (per
/// feedback_verify_ui_state_not_just_rendering): the auto-close from
/// EditMangaModalFixture only proves the PUT returned 2xx, not that the new
/// value made it through MangaResource.ApplyChanges + back through GET
/// /api/v5/manga/{id}.
///
/// Field flipped: `monitored` checkbox (CheckInput rendered as a hidden
/// native input behind a styled icon — selected via the input[name=...]
/// CSS selector, same approach as SettingsSaveRoundTripFixture which has
/// the same hidden-native-input shape).
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D — AddManga modal
/// nav race in ConfirmAddAsync). Every fixture in this plan that calls
/// AddMangaFlow.AddByMangaDexIdAsync remains [Explicit] until #102 lands.
/// Promotes to live [Test] (delete-the-attribute change) when #102 closes.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 18 Plan 18-14 D-D dependency (#102): AddMangaModal.ConfirmAddAsync click→nav race. AddMangaFlow.AddByMangaDexIdAsync times out at WaitForURLAsync until that lands. Drop this attribute when #102 closes.")]
public class EditMangaModalCrudFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task save_persists_monitored_flip()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Open EditMangaModal via the MangaDetails Edit button.
        await details.EditButton.ClickAsync();
        var modal = new EditMangaModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // Read the original `monitored` value from the hidden native input.
        // EditMangaModalContent renders a CheckInput; the CheckInput component
        // surfaces the native checkbox via input[type='checkbox'][name='monitored']
        // (same hidden-input pattern as SettingsSaveRoundTripFixture).
        var monitoredInput = Page.Locator("input[type='checkbox'][name='monitored']");
        await monitoredInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        var originalChecked = await monitoredInput.IsCheckedAsync();
        var flippedChecked = !originalChecked;

        // Force-click the hidden input (CheckInput renders the styled icon overlay
        // which intercepts pointer events otherwise).
        await monitoredInput.ClickAsync(new LocatorClickOptions { Force = true });
        await Page.WaitForTimeoutAsync(300);

        // STATE assertion 1: the click toggled the value pre-save.
        var afterClick = await monitoredInput.IsCheckedAsync();
        afterClick.Should().Be(flippedChecked, "click must toggle monitored before save");

        // Save — modal auto-closes when useSaveManga.onSuccess fires.
        await modal.SaveAsync();
        await Assertions.Expect(modal.ModalRoot).ToBeHiddenAsync();

        // Re-open the modal and re-read the value. The second open hits GET
        // /api/v5/manga/{id} through useSingleManga, so the round-trip is
        // observable here — not just modal-close.
        await details.EditButton.ClickAsync();
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        var reopenedInput = Page.Locator("input[type='checkbox'][name='monitored']");
        await reopenedInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion 2: persisted value matches the flipped value (the
        // full PUT /api/v5/manga/{id} round-trip — INVENTORY row 72 contract).
        var persistedChecked = await reopenedInput.IsCheckedAsync();
        persistedChecked.Should().Be(flippedChecked, "monitored value must persist through PUT /api/v5/manga/{id} and round-trip back via useSingleManga");
    }
}
