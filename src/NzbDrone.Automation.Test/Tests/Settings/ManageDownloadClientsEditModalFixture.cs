using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — INVENTORY modal-action row ManageDownloadClientsEditModal.
/// Verifies the bulk-edit modal accessible via Settings/DownloadClients > Manage.
///
/// Note (Rule 3 deviation): plan-spec used "edit-downloadclient-modal" testid for
/// the inner modal, but clicking Edit inside the Manage modal opens the bulk-edit
/// ManageDownloadClientsEditModalContent — a different modal shape than the
/// per-client EditDownloadClientModalContent. Plan 20-05 Task 5.1 added a distinct
/// "manage-downloadclients-edit-modal" testid; this fixture asserts on that.
///
/// The Manage modal's Edit button is disabled until rows are selected. Baseline
/// seed at AutomationTest L83-85 + TestKit L86-101 guarantees one InProcess
/// row exists. We click the row's checkbox to enable the bulk-Edit button.
///
/// Tier (D-04): modal-action axis = Nightly.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ManageDownloadClientsEditModalFixture : AutomationTest
{
    [Test]
    public async Task manage_edit()
    {
        var settings = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(settings.ManageButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await settings.ManageButton.ClickAsync();

        var manageModal = Page.GetByTestId("manage-downloadclients-modal");
        await Assertions.Expect(manageModal).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // State assertion (audit-test-assertions.sh): verify the manage modal
        // renders at least one DownloadClient row, not just an empty table.
        var rowCheckboxes = manageModal.Locator("input[type='checkbox']");
        var checkboxCount = await rowCheckboxes.CountAsync();
        checkboxCount.Should().BeGreaterThan(
            1,
            "manage modal must show table-header select-all + at least one DC row");

        // Select the first row's checkbox to enable the bulk-Edit button.
        // debug-30 (2026-05-16): CheckInput renders a visually-hidden <input>
        // alongside a <div class="CheckInput-isNotChecked"> visual surrogate
        // that intercepts pointer events. Clicking the wrapping <label>
        // (CheckInput.tsx:105) triggers the handler without the overlay race.
        var rowLabels = manageModal.Locator("label:has(input[type='checkbox'])");
        var firstRowLabel = rowLabels.Nth(1); // [0] is the table-header select-all
        await Assertions.Expect(firstRowLabel).ToBeVisibleAsync();
        await firstRowLabel.ClickAsync();

        // Edit button is now enabled.
        var editButton = manageModal.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true });
        await Assertions.Expect(editButton).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await editButton.ClickAsync();

        // The bulk-edit ManageDownloadClientsEditModalContent should open.
        var bulkEditModal = Page.GetByTestId("manage-downloadclients-edit-modal");
        await Assertions.Expect(bulkEditModal).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Cancel the bulk-edit modal — assert it dismisses.
        await bulkEditModal.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Assertions.Expect(bulkEditModal).ToBeHiddenAsync(new() { Timeout = 15_000 });
    }
}
