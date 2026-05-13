using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.AddManga;

/// <summary>
/// Phase 18 Plan-04 AddManga cluster — end-to-end add-manga flow fixture.
/// Exercises AddMangaFlow.AddByMangaDexIdAsync (the D-08 canonical helper).
/// State assertion: URL match + page testid visible after the post-add
/// navigation lands on /manga/{slug}.
///
/// Cassette deferral: see AddMangaSearchFixture for the [Explicit] rationale.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
[Explicit("Phase 18 Plan-04 Task 3 cassette deferral — MangaDex cassette not yet recorded. See AddMangaSearchFixture for full deferral context.")]
public class AddMangaFlowFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task add_by_mangadex_id_lands_on_manga_details()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering):
        // URL match + page testid + reference the page object so the fluent contract is live.
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
        await Assertions.Expect(details.PageRoot).ToBeVisibleAsync();
    }
}
