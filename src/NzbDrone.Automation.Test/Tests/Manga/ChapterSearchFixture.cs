using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — v5-endpoint POST /api/v5/chapter/{id}/search
// (INVENTORY line 81). "MangaDetails chapter Search button"
//
// Asserts that clicking the per-chapter Auto-Search button enqueues a
// ChapterSearchCommand via the backend endpoint. The frontend's
// ChapterSearchCell dispatches POST /api/v5/chapter/{id}/search (Plan 07-01
// + 06 D-12); the backend returns 202 Accepted + command id, with the
// queued ChapterSearchCommand visible on /api/v5/command.
[TestFixture]
[Category("AutomationTest")]
public class ChapterSearchFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task search_pushes_command()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // Switch to the Chapters tab. The Search-cell button is per-row in the
        // ChapterRow actions column (ChapterSearchCell.tsx).
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // Find a chapter row + click its auto-search button.
        var rowsLocator = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "the seeded manga must have at least one chapter row");

        var firstRow = rowsLocator.First;
        var rowTestId = await firstRow.GetAttributeAsync("data-testid");
        rowTestId.Should().NotBeNullOrEmpty();
        var chapterId = rowTestId!.Replace("chapter-row-", string.Empty);

        var searchButton = Page.GetByTestId($"chapter-row-{chapterId}-search-button");
        await Assertions.Expect(searchButton).ToBeVisibleAsync();

        await searchButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion: query /api/v5/command and assert a ChapterSearch entry
        // is queued. The endpoint POST /api/v5/chapter/{id}/search enqueues a
        // ChapterSearchCommand (Phase 6 D-12 command-queue-based shape); the
        // canonical name appears in the GET /api/v5/command stream.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var commands = await http.GetStringAsync($"{RootUri}/api/v5/command");

        commands.Should().NotBeNullOrEmpty();
        commands.Should().Contain(
            "ChapterSearch",
            "the click must enqueue a ChapterSearchCommand via POST /api/v5/chapter/{id}/search");
    }
}
