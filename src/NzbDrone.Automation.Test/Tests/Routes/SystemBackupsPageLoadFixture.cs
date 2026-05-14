using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
public class SystemBackupsPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_backups_page()
    {
        var page = await new SystemBackupsPage(Page).OpenAsync(RootUri);

        Page.Url.Should().EndWith("/system/backup");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
