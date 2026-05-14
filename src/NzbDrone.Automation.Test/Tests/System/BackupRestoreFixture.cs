using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 138
// (`v5-endpoint POST /api/v5/system/backup/restore/{id} → Backup Restore button`).
//
// Non-cassette-dependent: this fixture only exercises the UI contract — the
// "Restore Backup" toolbar button opens the RestoreBackupModal. We never
// actually click Restore (per T-18-17-01 threat-disposition — a real restore
// would wipe the test DB). The fixture asserts on STATE (modal open after
// click, modal close after cancel) without ever committing a destructive
// action.
//
// Note: on fresh DB there are no backups available, so the modal will show
// an empty upload-only restore form. That's still a valid open/close state
// contract — the dialog's existence is what we're testing.
[TestFixture]
[Category("AutomationTest")]
public class BackupRestoreFixture : AutomationTest
{
    [Test]
    public async Task restore_modal_opens_and_closes_without_destructive_action()
    {
        await new SystemBackupsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(Page.GetByTestId("system-backups-page")).ToBeVisibleAsync();

        // STATE assertion 1: toolbar exposes the Restore Backup button (text
        // match — the button has no dedicated testid yet; per D-18 we fall
        // back to GetByRole for pure assertion targets).
        var restoreButton = Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Restore Backup" });
        await Assertions.Expect(restoreButton).ToBeVisibleAsync();

        // Click to open the RestoreBackupModal. Threat T-18-17-01 mitigation:
        // we will NEVER click the Restore confirm button inside the modal —
        // only the cancel/close path.
        await restoreButton.ClickAsync();

        // STATE assertion 2 (modal-opens contract): the modal dialog rendered.
        // The Restore Backup modal has a dialog role (default Modal component
        // shape). The ModalHeader text "Restore Backup" is the contract.
        var modalDialog = Page.GetByRole(AriaRole.Dialog);
        await modalDialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var modalText = await modalDialog.TextContentAsync();
        modalText.Should().NotBeNullOrEmpty();
        modalText.Should().Contain("Restore");

        // STATE assertion 3 (modal-closes contract): pressing Esc closes the
        // modal without invoking the destructive restore path. After close
        // the dialog should be detached from the DOM.
        await Page.Keyboard.PressAsync("Escape");
        await modalDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 5_000
        });

        Page.Url.Should().EndWith("/system/backup");
    }
}
