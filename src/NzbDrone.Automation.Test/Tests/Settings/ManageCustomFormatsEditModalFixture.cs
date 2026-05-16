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

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
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
        var rowCheckboxes = manageDialog.GetByRole(AriaRole.Checkbox);
        var cbCount = await rowCheckboxes.CountAsync();
        cbCount.Should().BeGreaterThan(1); // header select-all + ≥1 row

        // debug-30 (2026-05-16): CheckInput's visually-hidden <input> is
        // covered by a <div class="CheckInput-isNotChecked"> visual surrogate
        // that intercepts pointer events. Click the wrapping <label>
        // (CheckInput.tsx:105) — the handler fires without the overlay race.
        var rowLabels = manageDialog.Locator("label:has(input[type='checkbox'])");
        await rowLabels.Nth(1).ClickAsync();

        // Click the bulk Edit button (label "Edit").
        await manageDialog.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First.ClickAsync();

        // The bulk-edit modal opens on top of the manage modal.
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var bulkEditDialog = Page.GetByRole(AriaRole.Dialog).Last;
        await Assertions.Expect(bulkEditDialog).ToBeVisibleAsync();

        // State assertion: the bulk-edit modal exposes the
        // IncludeCustomFormatWhenRenaming field (per
        // ManageCustomFormatsEditModalContent — the canonical bulk-editable
        // field on a CustomFormat).
        var bulkText = await bulkEditDialog.TextContentAsync();
        bulkText.Should().Contain("Include Custom Format When Renaming");
    }
}
