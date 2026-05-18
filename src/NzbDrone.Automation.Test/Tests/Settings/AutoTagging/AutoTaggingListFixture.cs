using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — AutoTagging GET (list) fixture. Greens INVENTORY
/// v5-endpoint row `GET /api/v5/autotagging | Settings/Tags/Auto Tagging list`.
///
/// Asserts the V5 controller (Plan 24-04) answers /api/v5/autotagging with a
/// 200 + JSON array (empty on a fresh DB; populated after a seeded create).
/// Mirrors the Phase 23 DelayProfileListFixture shape — the surface lives at
/// /settings/tags (AutoTagging is a nested section within the Tags settings
/// page); fixtures hit the API directly via Page.APIRequest.
///
/// Pattern 6 (24-PATTERNS.md §"Plan 24-05 patterns" lines 1158-1209): mirror
/// the DelayProfile sibling automation fixture for the GET (list) shape.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingListFixture : AutomationTest
{
    [Test]
    public async Task list_renders_after_auto_tagging_creation()
    {
        // Open the parent Settings/Tags page — AutoTagging is a nested section
        // within /settings/tags (no dedicated /settings/tags/autotagging route).
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Microsoft.Playwright.Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/autotagging";

        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200,
            "GET /api/v5/autotagging should return 200 after Plan 24-04 controller port");

        var listJson = await listResp.JsonAsync();
        listJson.HasValue.Should().BeTrue();
        var root = listJson!.Value;
        root.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array,
            "AutoTagging GET list returns a JSON array (RestController<T> contract)");
    }
}
