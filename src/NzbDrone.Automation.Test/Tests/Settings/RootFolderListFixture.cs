using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — RootFolder list-load fixture.
/// Greens INVENTORY v5-endpoint row `GET /api/v5/rootfolder | Settings/MediaManagement
/// RootFolders + AddManga`.
///
/// Blocker #4 mitigation: SeedBaselineAsync (AutomationTest.cs:85) already
/// guarantees a baseline root folder row exists at <c>_tempFolderRoot</c>, so the
/// list is non-empty without an additional OneTimeSetUp seed (Blocker #4 path b).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class RootFolderListFixture : AutomationTest
{
    [Test]
    public async Task list_loads()
    {
        var page = await new SettingsRootFoldersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var listTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/rootfolder") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });
        await Page.ReloadAsync();
        var resp = await listTask;

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("path");
    }
}
