using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: / (MangaIndex grid loads).
// PRSmoke (D-14): top-nav root route — PR-tier smoke gate.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaIndexPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_manga_index()
    {
        var page = await new MangaIndexPage(Page).OpenAsync(RootUri);

        // State assertion: URL ends at root (no trailing path beyond `/`) +
        // page title contains "Mangarr". When Plan-04 wires the manga-index-page
        // testid, MainContainer.WaitForAsync will gate before this point and
        // assertion strengthens (ToBeVisibleAsync on MainContainer).
        Page.Url.Should().EndWith("/");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        // Keep fluent-return-this contract live (D-17).
        page.Should().NotBeNull();
    }
}
