using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /manga/wanted/missing (MangaMissing table renders).
// PRSmoke (D-14): top-nav root route (Wanted -> Missing) — PR-tier smoke gate.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaMissingPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_manga_missing()
    {
        var page = await new MangaMissingPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/manga/wanted/missing");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
