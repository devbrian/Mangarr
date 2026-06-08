using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 18 Plan-04 AddManga cluster — end-to-end add-manga flow fixture.
/// Exercises AddMangaFlow.AddByMangaBakaIdAsync (the D-08 canonical helper).
/// State assertion: URL match + page testid visible after the post-add
/// navigation lands on /manga/{slug}.
///
/// Cassettes recorded in Plan 18-14 gap closure; runs LIVE under default CI Replay mode.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AddMangaFlowFixture : AutomationTest
{
    private const string KnownMangaBakaId = "3397";

    [Test]
    public async Task add_by_mangabaka_id_lands_on_manga_details()
    {
        var details = await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering):
        // URL match + page testid + reference the page object so the fluent contract is live.
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
        await Assertions.Expect(details.PageRoot).ToBeVisibleAsync();
    }
}
