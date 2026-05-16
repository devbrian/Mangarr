using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — CustomFormat list-load fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/customformat |
/// Settings/CustomFormats list`.
///
/// Blocker #4 mitigation: seeds a CustomFormat in OneTimeSetUp via
/// <see cref="TestKit.TestKit.SeedCustomFormatAsync"/> so the list response
/// is guaranteed non-empty (no Inconclusive branch).
///
/// PR #173 CI-fix (2026-05-15): the OneTimeSetUp seed previously 400'd because
/// SeedCustomFormatAsync sent an empty specifications array
/// (CustomFormatController's validator requires ≥1 spec — verified by reading
/// CustomFormatController.cs:42-55). TestKit.SeedCustomFormatAsync now sends a
/// canonical minimal valid spec (ReleaseTitleSpecification + `[a-z]` value), so
/// the seeded list is non-empty and the assertion below has SeedName to match.
/// List call switched from page-reload + WaitForResponseAsync race to a direct
/// APIRequest call, mirroring the protocol-race fix applied to TagListFixture.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CustomFormatListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07a CF List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedCustomFormatAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsCustomFormatsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/customformat",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
