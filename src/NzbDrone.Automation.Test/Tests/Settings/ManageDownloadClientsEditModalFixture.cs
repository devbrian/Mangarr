using System.Text.RegularExpressions;
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
        // renders at least one DownloadClient row via the per-row checkbox
        // testid emitted by ManageDownloadClientsModalRow (GH #180 scope B).
        // The `settings-downloadclient-row-{id}-checkbox` shape is exclusive to
        // body rows — the table-header select-all checkbox does NOT carry this
        // prefix, so a count of >= 1 means a real DC row exists.
        var rowCheckboxes = manageModal.GetByTestId(new Regex(@"^settings-downloadclient-row-\d+-checkbox$"));
        var checkboxCount = await rowCheckboxes.CountAsync();
        checkboxCount.Should().BeGreaterThan(
            0,
            "manage modal must show at least one DownloadClient row");

        // Select the first row's checkbox to enable the bulk-Edit button.
        // GH #180 scope A/B: the testid lands on the wrapping <label> in
        // CheckInput (the visible click target — the underlying <input
        // type="checkbox"> is visually hidden behind a <div> visual surrogate
        // that intercepts pointer events). Replaces the prior
        // `label:has(input[type='checkbox'])` DOM-traversal pattern.
        var firstRowCheckbox = rowCheckboxes.First;
        await Assertions.Expect(firstRowCheckbox).ToBeVisibleAsync();
        await firstRowCheckbox.ClickAsync();

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
