using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.V11Closeout;

// Phase 28 Plan 28-01 Task 2 — V11Closeout per-vertical fixture.
//
// Vertical 3: AutoTagging (Phase 24 restore-rebuild). Validates /settings/tags
// (the parent route — AutoTagging is a nested section per
// AutoTaggingListFixture pattern) AND the deeper sub-route /settings/tags/auto
// both mount the SettingsTagsPage. Asserts /api/v5/autotagging answers with
// an empty array on fresh DB (Phase 24 D-NN user-additive precedence: zero
// rules in baseline state).
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingClosingFixture : AutomationTest
{
    [Test]
    public async Task autotagging_page_and_api_render_clean_on_fresh_db()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Sub-route /settings/tags/auto must also resolve to the same shell.
        await Page.GotoAsync($"{RootUri}/settings/tags/auto");
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/autotagging");
        listResp.Status.Should().Be(200, "GET /api/v5/autotagging after Phase 24 controller port");

        var json = await listResp.JsonAsync();
        json.HasValue.Should().BeTrue();
        var root = json!.Value;
        root.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);
        root.GetArrayLength().Should().Be(0,
            "fresh DB has no AutoTagging rules — Phase 24 user-additive precedence model starts empty");
    }
}
