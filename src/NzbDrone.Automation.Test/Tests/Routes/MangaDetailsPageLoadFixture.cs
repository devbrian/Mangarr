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
/// Cassette deferral: see AddMangaSearchFixture for the [Explicit] rationale.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 18 Plan-04 Task 3 cassette deferral — MangaDex cassette not yet recorded. See AddMangaSearchFixture for full deferral context.")]
public class MangaDetailsPageLoadFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task loads_manga_details_for_added_manga()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        await Assertions.Expect(details.PageRoot).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
    }
}
