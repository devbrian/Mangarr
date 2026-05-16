using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `POST /api/v5/system/backup/restore/upload` (Backup Upload+Restore).
///
/// The destructive UPLOAD path requires a backup file payload AND is gated by
/// the existing BackupRestoreFixture's T-18-17-01 threat mitigation: NEVER
/// click Restore confirm on a test DB. This fixture exercises the modal
/// open/close contract that surfaces the upload form WITHOUT clicking Restore.
///
/// Flow: navigate /system/backup → click "Restore Backup" toolbar button →
/// RestoreBackupModal opens → state assertion: the upload file-input element
/// is present (proving the upload-restore surface mounted) → Cancel → modal
/// hidden.
///
/// Per Phase 18 D-12 threat mitigation: no actual upload performed
/// (a real upload + restore would wipe the test DB). The contract being
/// asserted is the modal's upload-form surface, NOT the POST round-trip.
///
/// Blocker #4: page-level seed only (no AddManga needed); zero
/// inconclusive-skip branches.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BackupUploadRestoreFixture : AutomationTest
{
    [Test]
    public async Task upload_restore()
    {
        await new SystemBackupsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(Page.GetByTestId("system-backups-page")).ToBeVisibleAsync();

        var restoreButton = Page.GetByRole(AriaRole.Button, new() { Name = "Restore Backup" });
        await Assertions.Expect(restoreButton).ToBeVisibleAsync();
        await restoreButton.First.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion: the upload surface is present inside the dialog —
        // RestoreBackupModalContent.tsx exposes a file-type input element for
        // the upload path. Its presence proves the upload-restore form mounted
        // (not just the empty list of backups branch).
        var fileInput = modal.Locator("input[type='file']").First;
        await Assertions.Expect(fileInput).ToBeAttachedAsync(
            new LocatorAssertionsToBeAttachedOptions { Timeout = 5_000 });

        // T-18-17-01 threat mitigation: NEVER click Restore. Cancel the modal
        // to assert the close-contract.
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(modal).ToBeHiddenAsync(new() { Timeout = 5_000 });

        Page.Url.Should().EndWith("/system/backup");
    }
}
