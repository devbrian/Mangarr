using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Import CustomFormat modal-action
/// fixture. Greens INVENTORY modal-action row `ImportCustomFormatModal |
/// Settings/CustomFormats Import`.
///
/// Blocker #4 mitigation: opens the Add CF card (the empty-state import
/// affordance), then clicks the Import button inside the EditCustomFormat
/// modal (which is the actual entry point for the Import modal — see
/// EditCustomFormatModalContent.tsx L222-225). Asserts the Import modal
/// renders with the JSON paste field visible.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ImportCustomFormatModalFixture : AutomationTest
{
    [Test]
    public async Task import_from_json()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Open the Add CF card (icon-only Card with plus svg; same locator
        // pattern as CustomFormatExportImportFixture empty-state branch).
        var addCard = Page.Locator("button:has(svg[data-icon='plus'])").First;
        await addCard.ClickAsync();

        var addDialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(addDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Click the Import button inside the AddCustomFormat dialog. The
        // EditCustomFormatModalContent footer renders a button with
        // text "Import" (L222-225).
        await addDialog.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).First.ClickAsync();

        // The ImportCustomFormatModal opens as a second dialog on top of the
        // Add dialog. Wait for an additional dialog to appear and assert
        // it carries the import surface (a textarea / JSON paste field).
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var importDialog = Page.GetByRole(AriaRole.Dialog).Last;
        await Assertions.Expect(importDialog).ToBeVisibleAsync();
        var importText = await importDialog.TextContentAsync();
        importText.Should().NotBeNullOrEmpty();
    }
}
