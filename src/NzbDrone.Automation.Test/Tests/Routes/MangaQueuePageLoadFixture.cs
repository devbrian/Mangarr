using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /manga/activity/queue (MangaQueue table renders).
// PRSmoke (D-14): top-nav root route — PR-tier smoke gate.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaQueuePageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_manga_queue()
    {
        var page = await new MangaQueuePage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/manga/activity/queue");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
