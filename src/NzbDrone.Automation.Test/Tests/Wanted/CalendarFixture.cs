using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

[TestFixture]
[Category("AutomationTest")]
public class CalendarFixture : AutomationTest
{
    [Test]
    public async Task calendar_v2_placeholder_loads_without_white_screen()
    {
        // Calendar is a v2 placeholder per UI-06; this fixture guards against
        // white-screen regression and verifies the placeholder testid is
        // rendered. No AddMangaFlow seed needed — the page renders without data.
        await new CalendarPage(Page).OpenAsync(RootUri);

        // STATE assertion: placeholder testid visible. When the Calendar grows
        // into v2 functionality, this fixture will need to expand to assert
        // event-rendering state, not just placeholder presence.
        await Assertions.Expect(Page.GetByTestId("calendar-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("calendar-grid")).ToBeVisibleAsync();

        Page.Url.Should().EndWith("/calendar");
    }
}
