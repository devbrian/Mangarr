using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — Tag list-load fixture. Greens
/// INVENTORY v5-endpoint row `GET /api/v5/tag | Settings/Tags list`.
///
/// Blocker #4 mitigation: OneTimeSetUp seeds a tag via
/// <see cref="TestKit.TestKit.SeedTagAsync"/> so the API response is guaranteed
/// non-empty (no Inconclusive branch — Blocker #4). The fixture asserts the
/// seeded label appears in the body — state-assertion per
/// <c>feedback_verify_ui_state_not_just_rendering</c>.
///
/// PR #173 CI-fix (2026-05-15): the previous WaitForResponseAsync + reload
/// pattern raced Playwright's network-resource lifecycle ("No resource with
/// given identifier found" on resp.TextAsync — the response body had already
/// been GC'd by the time we tried to read it). Switched to a direct
/// APIRequest call (mirrors NamingPresetsFixture's stable pattern), which
/// reads the body on the same context and removes the race window. Page
/// navigation still verifies the Tags settings page renders.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagListFixture : AutomationTest
{
    private const string SeedLabel = "plan-20-07a-tag";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedTagAsync(SeedLabel);
    }

    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/tag",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedLabel);
    }
}
