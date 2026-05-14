using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
public class SystemEventsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_events_page()
    {
        var page = await new SystemEventsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/system/events");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
