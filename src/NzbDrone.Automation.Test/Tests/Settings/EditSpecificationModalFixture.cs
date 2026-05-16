using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Edit Specification modal-action
/// fixture. Greens INVENTORY modal-action row `EditSpecificationModal | Edit
/// CustomFormat Edit Spec`.
///
/// Flow: open Add CF -> EditCustomFormat modal -> AddSpecification modal ->
/// click a specific spec card (the AddSpecification list); the
/// EditSpecificationModal then opens with the chosen spec's fields. The
/// fixture verifies the EditSpecification dialog renders by counting dialogs
/// after the spec-card click (transitions 2 -> 2 dialogs since the
/// AddSpecification closes as EditSpecification opens; we assert the active
/// dialog body contains the "Name" form label which all specs surface).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class EditSpecificationModalFixture : AutomationTest
{
    [Test]
    public async Task edit_spec()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var addCard = Page.Locator("button:has(svg[data-icon='plus'])").First;
        await addCard.ClickAsync();

        var editDialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(editDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await editDialog.Locator("button:has(svg[data-icon='plus'])").First.ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        // Click the canonical "Release Title" spec card in the picker — opens
        // the EditSpecificationModal in place of the AddSpecificationModal.
        // debug-30 fix-forward (CodeRabbit #11): AddSpecificationItem now
        // propagates a `settings-specification-card-{slug}` testid onto its
        // underlay button (mirrors the settings-customformat-card-{slug}
        // precedent landed in this same PR for Cluster B2).
        var addSpecDialog = Page.GetByRole(AriaRole.Dialog).Last;
        var releaseTitleCard = addSpecDialog.GetByTestId("settings-specification-card-release-title");
        await Assertions.Expect(releaseTitleCard).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await releaseTitleCard.ClickAsync();

        // EditSpecificationModal opens. Assert the dialog body now contains
        // a "Name" form label (every spec exposes one).
        var editSpecDialog = Page.GetByRole(AriaRole.Dialog).Last;
        await Assertions.Expect(editSpecDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var editSpecText = await editSpecDialog.TextContentAsync();
        editSpecText.Should().Contain("Name");
    }
}
