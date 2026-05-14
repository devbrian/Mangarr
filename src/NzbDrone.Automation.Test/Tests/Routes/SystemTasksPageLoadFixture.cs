using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Routes;

[TestFixture]
[Category("AutomationTest")]
public class SystemTasksPageLoadFixture : AutomationTest
{
    [Test]
    public async Task loads_system_tasks_page()
    {
        var page = await new SystemTasksPage(Page).OpenAsync(RootUri);

        // State assertion (per feedback_verify_ui_state_not_just_rendering): assert URL match AND
        // page title. When Plan-09 wires system-tasks-page testid, WaitForLoadedAsync resolves
        // via the panel locator (rather than the URL fallback).
        Page.Url.Should().EndWith("/system/tasks");
        var title = await Page.TitleAsync();
        title.Should().Contain("Mangarr");

        page.Should().NotBeNull();
    }
}
