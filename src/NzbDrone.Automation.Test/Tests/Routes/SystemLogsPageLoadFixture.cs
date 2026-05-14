using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
public class SystemLogsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_logs_page()
    {
        var page = await new SystemLogsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/system/logs/files");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
