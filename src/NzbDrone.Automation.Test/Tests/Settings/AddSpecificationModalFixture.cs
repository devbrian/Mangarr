using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Add Specification modal-action
/// fixture. Greens INVENTORY modal-action row `AddSpecificationModal | Edit
/// CustomFormat Add Spec`.
///
/// Flow: open Add CF card -> EditCustomFormat modal -> click the add-spec
/// Card (icon-only plus) inside the Conditions FieldSet -> AddSpecification
/// modal opens. State-assertion = the AddSpecification dialog renders with
/// the spec list (its body contains at least one canonical spec name from
/// GET /api/v5/customformat/schema).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AddSpecificationModalFixture : AutomationTest
{
    [Test]
    public async Task add_spec()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Open Add CF card (icon-only plus svg in the CustomFormats list).
        var addCard = Page.Locator("button:has(svg[data-icon='plus'])").First;
        await addCard.ClickAsync();

        var editDialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(editDialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Inside the EditCustomFormat dialog there is a second plus-icon Card
        // (the Add Condition affordance — see EditCustomFormatModalContent.tsx
        // L180-187). Click it; the AddSpecificationModal opens.
        await editDialog.Locator("button:has(svg[data-icon='plus'])").First.ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        var addSpecDialog = Page.GetByRole(AriaRole.Dialog).Last;
        await Assertions.Expect(addSpecDialog).ToBeVisibleAsync();

        // State assertion: dialog renders the spec catalog. Schema is fetched
        // asynchronously via GET /api/v5/customformat/schema; wait for the
        // canonical "Release Title" spec (ReleaseTitleSpecification.cs:6 —
        // ImplementationName = "Release Title") before reading the body so
        // the assertion isn't racing the LoadingIndicator. debug-30
        // (2026-05-16): prior assertion checked for "Specification" substring
        // which never appears — the schema sends `implementationName` values
        // like "Release Title" / "Language" / "Indexer Flag", NOT the C#
        // class-name suffix.
        await Assertions.Expect(addSpecDialog.GetByText("Release Title").First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
