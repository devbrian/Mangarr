using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

[TestFixture]
[Category("AutomationTest")]
public class SettingsRootFoldersFixture : AutomationTest
{
    [Test]
    public async Task loads_settings_root_folders()
    {
        // Mangarr's RootFolders live on /settings/mediamanagement (no separate
        // /settings/rootfolders slug). See AppRoutes.tsx + MediaManagement.tsx.
        var page = await new SettingsRootFoldersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        Page.Url.Should().MatchRegex(@"/settings/mediamanagement$");
    }
}
