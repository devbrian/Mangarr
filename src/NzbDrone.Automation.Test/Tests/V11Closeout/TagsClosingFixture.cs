using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.V11Closeout;

// Phase 28 Plan 28-01 Task 2 — V11Closeout per-vertical fixture.
//
// Vertical 1: Tags (Phase 22 REPOINT). Validates /settings/tags renders + the
// V5 /api/v5/tag endpoint answers GET with a JSON array. A fresh DB has no
// tags so the page renders the "No tags have been added yet" empty state — we
// confirm both UI and API agree on count=0 (state-not-rendering invariant).
//
// Pattern κ: zero `series-*` / `episode-*` / `season-*` / `add-series-*`
// testids. Only `settings-tags-page` (D-18 allowed `settings-*` prefix).
[TestFixture]
[Category("AutomationTest")]
public class TagsClosingFixture : AutomationTest
{
    [Test]
    public async Task tags_page_renders_and_api_returns_empty_array()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/settings/tags$");

        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/tag");
        listResp.Status.Should().Be(200,
            "GET /api/v5/tag is the canonical Tags V5 endpoint (Phase 22 REPOINT)");

        var json = await listResp.JsonAsync();
        json.HasValue.Should().BeTrue();
        json!.Value.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);
        json!.Value.GetArrayLength().Should().Be(0,
            "fresh DB has no tags — UI empty state matches API count=0");
    }
}
