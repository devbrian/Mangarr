using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Export CustomFormat modal-action
/// fixture. Greens INVENTORY modal-action row `ExportCustomFormatModal |
/// Settings/CustomFormats Export`.
///
/// Blocker #4 mitigation: seeds a CustomFormat so an export-button card row
/// is guaranteed present (no Inconclusive). Clicks the per-card "Export
/// Custom Format" IconButton (aria-label, see CustomFormat.tsx L91-97) and
/// asserts the export dialog renders with substantive JSON content (the
/// existing CustomFormatExportImportFixture pattern, narrowed to the
/// seed-present path).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ExportCustomFormatModalFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a Export CF";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task export_yields_json()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var exportButton = Page.GetByRole(AriaRole.Button, new() { Name = "Export Custom Format" });

        // Seed guarantees ≥1 export button exists.
        var exportCount = await exportButton.CountAsync();
        exportCount.Should().BeGreaterThan(0);

        await exportButton.First.ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // State assertion: the dialog body has substantive text (the JSON
        // payload — not an empty modal shell). The seeded CF name appears
        // in the export JSON.
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().NotBeNullOrEmpty();
        dialogText.Should().Contain(SeedName);
    }
}
