using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Manage CustomFormats Edit
/// (bulk-edit) modal-action fixture. Greens INVENTORY modal-action row
/// `ManageCustomFormatsEditModal | Manage CustomFormats row Edit`.
///
/// Blocker #4 mitigation: seeds a CustomFormat so the manage table has at
/// least one selectable row. Opens the Manage modal, selects the first row
/// via its checkbox, clicks the bulk Edit button, and asserts the edit modal
/// renders with the IncludeCustomFormatWhenRenaming label visible (a field
/// the bulk-edit modal exposes per ManageCustomFormatsEditModalContent).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ManageCustomFormatsEditModalFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a MgrEdit CF";
    private int _seedId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        _seedId = await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task manage_edit()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Manage Formats" }).First.ClickAsync();

        var manageDialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(manageDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Tick the first row checkbox to enable the bulk Edit button
        // (ManageCustomFormatsModalContent disables it until selection > 0).
        // GH #180 scope B/E: target the row's testid emitted by
        // ManageCustomFormatsModalRow → TableSelectCell → CheckInput. The
        // testid lands on the wrapping <label> (the visible click target —
        // CheckInput's <input type="checkbox"> is visually hidden behind a
        // <div> overlay that intercepts pointer events). Replaces the prior
        // `label:has(input[type='checkbox'])` DOM-traversal pattern.
        var rowCheckbox = manageDialog.GetByTestId($"settings-customformat-row-{_seedId}-checkbox");
        await Assertions.Expect(rowCheckbox).ToBeVisibleAsync();
        await rowCheckbox.ClickAsync();

        // Click the bulk Edit button in the manage-modal FOOTER. GH #180 follow-up:
        // a bare GetByRole(Button, Name="Edit") is AMBIGUOUS inside this dialog — each
        // ManageCustomFormatsModalRow renders an actions-cell pencil IconButton whose
        // aria-label is also "Edit" (ManageCustomFormatsModalRow.tsx), and it precedes
        // the footer button in DOM order, so `.First` resolved to the ROW pencil and
        // opened the SINGLE-CF editor instead of the bulk-edit modal. Target the footer
        // SpinnerButton by its D-18 testid (ManageCustomFormatsModalContent.tsx).
        await manageDialog.GetByTestId("settings-customformat-manage-edit-button").ClickAsync();

        // The bulk-edit modal opens on top of the manage modal.
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var bulkEditDialog = Page.GetByRole(AriaRole.Dialog).Last;
        await Assertions.Expect(bulkEditDialog).ToBeVisibleAsync();

        // State assertion: the bulk-edit modal exposes the
        // IncludeCustomFormatWhenRenaming field (per
        // ManageCustomFormatsEditModalContent — the canonical bulk-editable
        // field on a CustomFormat). debug-30 iter-2 (2026-05-16): the label
        // is "Include Custom Format when Renaming" (lowercase 'when' per
        // en.json) — prior assertion was case-sensitive on capital W.
        var bulkText = await bulkEditDialog.TextContentAsync();
        bulkText.Should().Contain("Include Custom Format when Renaming");
    }
}
