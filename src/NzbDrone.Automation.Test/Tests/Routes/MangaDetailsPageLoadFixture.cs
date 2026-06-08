using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Routes;

/// <summary>
/// Phase 18 INVENTORY route axis row: `/manga/:titleSlug` MangaDetails page renders.
/// Plan-03 deferred this fixture to Plan-04 because it depends on AddMangaFlow as the
/// state-seeder (a manga must exist for the details page to render). Per D-06
/// UI-populates-via-UI, the fixture seeds via AddMangaFlow rather than a direct
/// /api/v5/manga POST.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering): URL matches
/// /manga/{slug} pattern AND the manga-details-page testid is visible.
///
/// Cassettes recorded in Plan 18-14 gap closure; runs LIVE under default CI Replay mode.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaDetailsPageLoadFixture : AutomationTest
{
    private const string KnownMangaBakaId = "3397";

    [Test]
    public async Task loads_manga_details_for_added_manga()
    {
        var details = await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        await Assertions.Expect(details.PageRoot).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
    }
}
