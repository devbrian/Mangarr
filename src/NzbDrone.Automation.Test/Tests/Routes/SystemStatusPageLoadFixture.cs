using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class SystemStatusPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_status_page()
    {
        var systemStatus = await new SystemStatusPage(Page).OpenAsync(RootUri);

        // State assertion (per feedback_verify_ui_state_not_just_rendering): assert URL match AND
        // page title (testid presence is checked in WaitForLoadedAsync). When Plan-09 adds the
        // system-status-page testid, this assertion strengthens to ToBeVisibleAsync() on StatusPanel.
        Page.Url.Should().EndWith("/system/status");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        // Reference systemStatus to keep the fluent-return-this contract live (D-17).
        systemStatus.Should().NotBeNull();
    }
}
