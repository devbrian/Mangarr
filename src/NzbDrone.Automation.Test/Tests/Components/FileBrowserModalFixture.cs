using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Components;

// Phase 18 Plan 18-16 Task 2 — Tests/Components/ FileBrowserModal
// (modal-action row 179 in INVENTORY.md). The FileBrowserModal is mounted
// from any path-picker call site; the canonical first entry-point is
// Settings/MediaManagement → Add RootFolder (AddRootFolder.tsx L54-L62
// wires data-testid="settings-root-folders-add-button" on the Button → Link
// chain). Clicking it sets isAddNewRootFolderModalOpen=true; the rendered
// modal contains a "File Browser" ModalHeader + a path input.
//
// State assertions:
// 1. Dialog with accessible name "File Browser" appears in DOM after clicking Add.
// 2. Modal footer "Cancel" button is visible.
// 3. After clicking Cancel, the dialog is removed from DOM.
//
// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
// The fixture navigates Settings → MediaManagement (a pure-read route)
// and opens a Settings-side modal; no AddMangaFlow hop, so issue #102 D-D
// race does not apply.
//
// gh #152 (Class 2 — Playwright timeout) fix-forward: the prior fixture
// located the modal header via Page.GetByRole(AriaRole.Heading, name='File
// Browser'). ModalHeader.tsx renders a div (not an h*), so the
// Heading-role locator never matched — only the dialog's aria-labelledby
// wiring at Modal.tsx:184 sets the accessible name. Switched to the
// dialog-by-accessible-name pattern that exploits the aria-labelledby
// contract.
[TestFixture]
[Category("AutomationTest")]
public class FileBrowserModalFixture : AutomationTest
{
    [Test]
    public async Task path_pick_returns()
    {
        // Navigate to Settings/MediaManagement (canonical RootFolder entry-point
        // per SettingsRootFoldersPage.cs / AppRoutes.tsx).
        await Page.GotoAsync($"{RootUri}/settings/mediamanagement");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Click the AddRootFolder button (testid wired in AddRootFolder.tsx L58).
        var addButton = Page.GetByTestId("settings-root-folders-add-button");
        await addButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        await addButton.ClickAsync();

        // STATE assertion 1: the FileBrowser dialog is rendered. Modal.tsx:184
        // sets aria-labelledby={headerId} on the role=dialog wrapper, so the
        // dialog's accessible name resolves to the ModalHeader text
        // "File Browser" (translate('FileBrowser') per en.json). The prior
        // AriaRole.Heading locator never matched because ModalHeader renders
        // a `<div>`, not an `<h*>` — only the dialog's aria-labelledby exposes
        // the title to a11y tools.
        var dialog = Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "File Browser" });
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await Assertions.Expect(dialog).ToBeVisibleAsync();

        // STATE assertion 2: modal footer Cancel button visible (scoped to the
        // dialog so we don't accidentally match the page background).
        var cancelButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" });
        await cancelButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });

        // STATE assertion 3: closing the modal removes the dialog from DOM.
        await cancelButton.ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
    }
}
