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

        // Open the EditCustomFormatModal by clicking the seeded CF card via its
        // settings-customformat-card-{slug} testid (debug-30 fix-forward,
        // live-indexer-card-click precedent — Card.tsx overlay routes clicks
        // through a Card-underlay <button> that intercepts inner-div clicks).
        var slug = SeedName.ToLowerInvariant().Replace(" ", "-");
        await Page.GetByTestId($"settings-customformat-card-{slug}").ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // debug-30 iter-2 (2026-05-16): TextContent doesn't include input
        // `value=` attributes — assert against the Name input's actual value.
        var nameInput = dialog.Locator("input[name='name']");
        await Assertions.Expect(nameInput).ToBeVisibleAsync(new() { Timeout = 5_000 });
        (await nameInput.InputValueAsync()).Should().Be(
            SeedName,
            "Edit modal must pre-populate the Name field with the seeded CustomFormat name");

        // Issue the rename via PUT — the modal's Save handler dispatches the
        // same saveCustomFormat action which hits PUT /api/v5/customformat/{id}.
        // debug-30 iter-2 (2026-05-16): CustomFormat validator rejects empty
        // specifications[] (`'Specifications' must not be empty` + `Must
        // contain at least one Condition`). Mirror SeedCustomFormatAsync's
        // 1-spec shape (ReleaseTitleSpecification with value="[a-z]") so the
        // PUT body validates.
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
                    specifications = new object[]
                    {
                        new
                        {
                            name = "Placeholder",
                            implementation = "ReleaseTitleSpecification",
                            implementationName = "Release Title",
                            negate = false,
                            required = false,
                            fields = new object[]
                            {
                                new { order = 0, name = "value", value = "[a-z]" }
                            }
                        }
                    }
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
