using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Components;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Components/ FileBrowserModal
/// (modal-action row 179 in INVENTORY.md). The FileBrowserModal is mounted
/// from any path-picker call site; the canonical first entry-point is
/// Settings/MediaManagement → Add RootFolder (AddRootFolder.tsx L54-L62
/// wires data-testid="settings-root-folders-add-button" on the Button → Link
/// chain). Clicking it sets isAddNewRootFolderModalOpen=true; the rendered
/// modal contains a "File Browser" ModalHeader + a path input.
///
/// State assertions:
/// 1. Modal heading "File Browser" appears in DOM after clicking Add.
/// 2. Modal footer "Cancel" button is visible.
/// 3. After clicking Cancel, the modal heading is removed from DOM.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// The fixture navigates Settings → MediaManagement (a pure-read route)
/// and opens a Settings-side modal; no AddMangaFlow hop, so issue #102 D-D
/// race does not apply.
/// </summary>
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

        // STATE assertion 1: modal heading "File Browser" visible. The
        // FileBrowserModalContent (L102) renders <ModalHeader>{translate('FileBrowser')}</ModalHeader>
        // which resolves to "File Browser" per en.json.
        var heading = Page.GetByRole(AriaRole.Heading, new() { Name = "File Browser" });
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var headingText = await heading.TextContentAsync();
        headingText.Should().Be("File Browser");

        // STATE assertion 2: modal footer Cancel button visible. The footer
        // is rendered by FileBrowserModalContent L197.
        var cancelButton = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" });
        await cancelButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });

        // STATE assertion 3: closing the modal removes the heading.
        await cancelButton.ClickAsync();
        await Assertions.Expect(heading).ToBeHiddenAsync();
    }
}
