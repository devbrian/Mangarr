using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

[TestFixture]
[Category("AutomationTest")]
public class SettingsIndexersFixture : AutomationTest
{
    [Test]
    public async Task loads_settings_indexers()
    {
        var page = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/settings/indexers$");
    }
}
