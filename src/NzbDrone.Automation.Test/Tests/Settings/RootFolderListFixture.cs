using System.Collections.Generic;
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
///
/// PR #173 CI-fix (2026-05-15): page-load WaitForResponseAsync with a Contains
/// filter on `/api/v5/rootfolder` never matched in CI because the frontend
/// (RootFolder/useRootFolders.ts:31) emits the camelCase path `/rootFolder`
/// and C# String.Contains is case-sensitive — the response event never fired
/// and the fixture timed out at 30s. Switched to direct APIRequest call
/// (mirrors NamingPresetsFixture's stable pattern), which makes the URL exact
/// and removes the page-trigger dependency. Page navigation still verifies the
/// settings page renders (smoke-trigger preserved).
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

        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/rootFolder",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain("path");
    }
}
