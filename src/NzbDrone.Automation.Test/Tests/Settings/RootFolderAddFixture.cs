using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 Nightly) — RootFolder POST round-trip.
/// Greens INVENTORY v5-endpoint row `POST /api/v5/rootfolder | Settings/MediaManagement
/// Add RootFolder`.
///
/// Blocker #4 mitigation: API-driven POST via Page.APIRequest (inherits X-Api-Key
/// from browser context). The fixture does not depend on the AddRootFolder UI form
/// rendering — only on the v5 endpoint accepting a new path — matching the
/// ReleaseProfileCrudFixture (Plan 20-07a) precedent.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RootFolderAddFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        var page = await new SettingsRootFoldersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Create a second root folder under a fresh subdir (baseline seed already
        // claims _tempFolderRoot; pick a sibling path so the POST validator accepts).
        var newPath = Path.Combine(Path.GetTempPath(), $"mangarr-rf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(newPath);

        var postResp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/rootfolder",
            new APIRequestContextOptions
            {
                DataObject = new { path = newPath }
            });

        postResp.Status.Should().BeInRange(200, 299);
        var body = await postResp.TextAsync();
        body.Should().Contain(newPath.Replace("\\", "\\\\"));

        // State assertion: GET surface includes the new row.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/rootfolder");
        listResp.Status.Should().Be(200);
        (await listResp.TextAsync()).Should().Contain(newPath.Replace("\\", "\\\\"));
    }
}
