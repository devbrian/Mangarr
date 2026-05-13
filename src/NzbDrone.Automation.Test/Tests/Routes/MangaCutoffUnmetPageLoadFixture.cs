using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /manga/wanted/cutoffunmet (MangaCutoffUnmet table renders).
// NOT PRSmoke (D-14): nightly/full-suite tier; only Missing is PRSmoke from Wanted group.
[TestFixture]
[Category("AutomationTest")]
public class MangaCutoffUnmetPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_manga_cutoff_unmet()
    {
        var page = await new MangaCutoffUnmetPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/manga/wanted/cutoffunmet");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
