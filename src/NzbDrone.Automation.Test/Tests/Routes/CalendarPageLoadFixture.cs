using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

// Phase 18 Plan-03 Task 3 — Route axis: /calendar (CalendarPage placeholder loads).
// PRSmoke (D-14): top-nav root route — PR-tier smoke gate.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CalendarPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_calendar()
    {
        var page = await new CalendarPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/calendar");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
