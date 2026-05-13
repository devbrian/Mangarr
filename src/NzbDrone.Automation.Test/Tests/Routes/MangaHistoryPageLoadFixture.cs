using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /manga/activity/history (MangaHistory table renders).
// NOT PRSmoke (D-14): nightly/full-suite tier; only Queue is PRSmoke from Activity group.
[TestFixture]
[Category("AutomationTest")]
public class MangaHistoryPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_manga_history()
    {
        var page = await new MangaHistoryPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/manga/activity/history");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
