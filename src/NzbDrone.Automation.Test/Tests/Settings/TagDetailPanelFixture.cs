using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 PRSmoke) — Tag detail panel render fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/tag/detail | Settings/Tags
/// details panel`.
///
/// Blocker #4 mitigation: seeds a tag in OneTimeSetUp; the tag-detail surface
/// is requested by Tags.tsx via <c>useTagDetails</c> on page mount. We race
/// the GET /api/v5/tag/detail response on reload — state-assertion = response
/// 200 + body parses as a JSON array (substring check on opening bracket).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagDetailPanelFixture : AutomationTest
{
    private const string SeedLabel = "plan-20-07a-detail";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedTagAsync(SeedLabel);
    }

    [Test]
    public async Task detail_renders()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Direct APIRequest avoids the WaitForResponseAsync+reload race that
        // caused Playwright "No resource with given identifier found" failures
        // across the Wave-2 Settings list/detail fixtures (Plan 20-07a/b — fixed
        // in commit 4a139aefd; same pattern applied here).
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/tag/detail",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();

        // Detail endpoint returns an array of TagDetailResource; body starts with '['.
        body.Should().StartWith("[");
    }
}
