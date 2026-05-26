using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Blocklist Filter modal
/// coverage (INVENTORY modal-action row 165: BlocklistFilterModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// Seeds a MangaBlocklist row via TestKit.SeedBlocklistAsync so the populated
/// branch mounts. Then exercises the same FloatingPortal-scoped filter-menu
/// pattern as HistoryFilterFixture / MangaMissingFilterFixture — open the
/// Filter toolbar button, scope to `#portal-root`, click the first preset,
/// assert the dropdown closes (real filter-state transition) and the URL
/// stays on the blocklist page.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BlocklistFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task filter_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        var menuPortal = Page.Locator("#portal-root");
        var menuItems = menuPortal.GetByRole(AriaRole.Button);

        // STATE assertion 2: filter dropdown opens.
        await menuItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var menuItemCount = await menuItems.CountAsync();
        menuItemCount.Should().BeGreaterThan(
            0,
            "Blocklist filter dropdown must render at least one preset filter button");

        // Click the first preset. handleFilterSelect dispatches
        // setBlocklistOption('selectedFilterKey', filterKey).
        var firstMenuItem = menuItems.First;
        var menuItemText = await firstMenuItem.TextContentAsync();
        await firstMenuItem.ClickAsync();

        // STATE assertion 3: dropdown closes (real state transition).
        await Assertions.Expect(menuItems.First).ToBeHiddenAsync(new()
        {
            Timeout = 10_000
        });

        // STATE assertion 4: URL preserved.
        Page.Url.Should().EndWith("/manga/activity/blocklist");
        menuItemText.Should().NotBeNullOrWhiteSpace("Blocklist filter preset must render text content");
    }

    private async Task<(int MangaId, int ChapterId)> ResolveSeedFksAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var mangaId = mangaDoc.RootElement[0].GetProperty("id").GetInt32();

        var chapterId = await SeedFkResolver.ResolveFirstChapterIdAsync(RootUri, ApiKey, mangaId);

        return (mangaId, chapterId);
    }
}
