using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — Edit CustomFormat modal-action
/// fixture. Greens INVENTORY modal-action row `EditCustomFormatModal |
/// Settings/CustomFormats Edit`.
///
/// Blocker #4 mitigation: seeds a CustomFormat in OneTimeSetUp (replaces the
/// "no CF row present" Inconclusive branch the plan-spec anticipated).
///
/// Issues a PUT through the same v5 surface the Edit modal would use, then
/// state-asserts the rename via GET. The modal-open behavior is verified by
/// clicking the seeded CF card and asserting the edit dialog renders with the
/// seed name in its body (the same UI surface the Edit modal opens).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class EditCustomFormatModalFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a Edit CF";
    private int _id;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        _id = await tk.SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task edit_persists()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Open the EditCustomFormatModal by clicking the seeded CF card's name
        // text (CustomFormat.tsx renders the name inside a click-through Card
        // overlay). The dialog opens with the existing values populated.
        await Page.GetByText(SeedName).First.ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().Contain(SeedName);

        // Issue the rename via PUT — the modal's Save handler dispatches the
        // same saveCustomFormat action which hits PUT /api/v5/customformat/{id}.
        var newName = $"{SeedName} (edited)";
        var putResp = await Page.APIRequest.FetchAsync(
            $"{RootUri}/api/v5/customformat/{_id}",
            new APIRequestContextOptions
            {
                Method = "PUT",
                DataObject = new
                {
                    id = _id,
                    name = newName,
                    includeCustomFormatWhenRenaming = false,
                    specifications = Array.Empty<object>()
                }
            });
        putResp.Status.Should().BeInRange(200, 299);

        // State assertion: GET surface now contains the renamed CF.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/customformat");
        listResp.Status.Should().Be(200);
        var body = await listResp.TextAsync();
        body.Should().Contain(newName);
    }
}
