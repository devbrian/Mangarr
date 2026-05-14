using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
public class SystemUpdatesPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_updates_page()
    {
        var page = await new SystemUpdatesPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/system/updates");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
