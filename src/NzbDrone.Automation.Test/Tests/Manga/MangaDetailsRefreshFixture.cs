using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — req META-04 (INVENTORY line 51).
// "User can manually trigger metadata refresh per-manga"
//
// Asserts that clicking the MangaDetails Refresh toolbar button enqueues a
// RefreshManga command via POST /api/v5/command. The frontend dispatches via
// useExecuteCommand → CommandNames.RefreshManga.
//
// State assertion approach: API spy on /api/v5/command (since Mangarr has no
// toast surface — see Plan 18-15 read pass on frontend/src/Components/). The
// command queue retains a record of the refresh, which we GET after the click
// and assert contains a `RefreshManga` entry.
[TestFixture]
[Category("AutomationTest")]
public class MangaDetailsRefreshFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task refresh_button_triggers_refresh_command()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var refreshButton = Page.GetByTestId("manga-details-refresh-button");
        await Assertions.Expect(refreshButton).ToBeVisibleAsync();

        await refreshButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion: query /api/v5/command and verify a RefreshManga entry
        // is queued. The backend retains a window of recent commands; any payload
        // referencing the canonical RefreshManga name proves the click reached
        // the API surface and was not silently dropped.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var commands = await http.GetStringAsync($"{RootUri}/api/v5/command");

        commands.Should().NotBeNullOrEmpty();
        commands.Should().Contain(
            "RefreshManga",
            "the click must enqueue a RefreshManga command via POST /api/v5/command");
    }
}
