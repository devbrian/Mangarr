using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /add/manga (AddManga search input visible).
// PRSmoke (D-14): top-nav root route — PR-tier smoke gate.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class AddMangaPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_add_manga()
    {
        var page = await new AddMangaPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/add/manga");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
