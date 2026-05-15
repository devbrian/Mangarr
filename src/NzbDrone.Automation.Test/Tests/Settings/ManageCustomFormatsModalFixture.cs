using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Manage CustomFormats modal-action
/// fixture. Greens INVENTORY modal-action row `ManageCustomFormatsModal |
/// Settings/CustomFormats Manage`.
///
/// Blocker #4 mitigation: seeds a CustomFormat in OneTimeSetUp so the manage
/// table is guaranteed non-empty (replaces "empty table" Inconclusive branch).
/// Clicks the "Manage Formats" toolbar button (translate('ManageFormats'),
/// see ManageCustomFormatsToolbarButton.tsx) and asserts the dialog renders
/// with the seeded CF name in its body.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ManageCustomFormatsModalFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a Manage CF";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task manage_modal()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Click the "Manage Formats" toolbar button (PageToolbarButton renders
        // its label as visible text + aria-label).
        await Page.GetByRole(AriaRole.Button, new() { Name = "Manage Formats" }).First.ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // State assertion: the manage table body contains the seeded CF name.
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().Contain(SeedName);
    }
}
