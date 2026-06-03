using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 18 Plan 18-18 — History filter coverage (INVENTORY modal-action row
/// 161: HistoryFilterModal).
///
/// Phase 39 Plan 39-07 (gap-closure): the prior seed-via-real-grab path
/// (AddMangaFlow → SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync) is
/// structurally dead — the in-process MangaDex/Comix indexers that produced
/// InteractiveSearch release rows were retired in Plan 39-03, so the grab has no
/// release to act on (the sole GatewayIndexer is seeded disabled-by-default). The
/// fixture now seeds its history row directly via TestKit.SeedHistoryFailedAsync
/// (the raw-SQLite seeder — Plan 19-01 verdict; the same mechanism HistoryRetryFixture
/// uses), which writes a DownloadFailed (eventType=2) ChapterHistory row. This keeps
/// the filter-state-transition contract intact WITHOUT depending on the retired
/// in-process search→grab pipeline.
///
/// Phase 19 Cat C triage (cross-plan, mirrors Plan 19-03's MangaMissingFilter /
/// MangaIndexFilter correction): the Mangarr Menu component
/// (Components/Menu/Menu.tsx) renders dropdown items as plain &lt;button&gt;
/// elements via MenuItem → Link; no product code emits role="menuitem". The
/// original fixture queried AriaRole.Menuitem and always found 0 items — a
/// fixture-logic bug, not a product bug. Corrected to scope AriaRole.Button to
/// the FloatingPortal (#portal-root) that Menu renders the open dropdown into.
///
/// State assertion: the seeded DownloadFailed row materializes (seededCount > 0),
/// the filter dropdown opens (preset filter buttons inside the floating menu
/// portal), and applying the "Grabbed" preset (eventType=1) empties the rendered
/// manga-history-row-* set — the seeded row is eventType=2 (downloadFailed), so the
/// server-side filter genuinely changes the row set. A real filter-state
/// transition, not just the dropdown visually opening.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task history_filter_menu_opens_and_applies_filter()
    {
        // Seed: add the manga, then seed a real DownloadFailed (eventType=2)
        // ChapterHistory row directly via the raw-SQLite TestKit helper so the
        // History page has a deterministic row to filter — independent of the
        // retired in-process InteractiveSearch→grab pipeline (Phase 39 Plan 39-07).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty("AddMangaFlow must land on the manga details URL");

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedHistoryFailedAsync(Runner.AppData, mangaId, chapterId);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present, and the seed produced a row.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        await Assertions.Expect(rowsLocator.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var seededCount = await rowsLocator.CountAsync();
        seededCount.Should().BeGreaterThan(0, "the TestKit seed must have produced a DownloadFailed history row");

        // The history toolbar has a FilterMenu that wraps HistoryFilterModal.
        // FilterMenu renders a "Filter" toolbar button (PageToolbarButton with
        // icons.FILTER); clicking opens a dropdown of preset filters.
        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        // FilterMenu renders its open dropdown into a FloatingPortal with
        // id="portal-root" (Components/Menu/Menu.tsx L156). Each preset filter
        // is a plain <button> there (MenuItem → Link → <button>; no
        // role="menuitem" is emitted by Mangarr's Menu component). Scope the
        // button query to the portal so the toolbar's own Filter button is
        // excluded.
        var menuPortal = Page.Locator("#portal-root");
        var menuItems = menuPortal.GetByRole(AriaRole.Button);

        // STATE assertion 2: filter menu actually rendered (dropdown opened).
        // useHistory.FILTERS wires All / Grabbed / Imported / Failed / Deleted
        // / Renamed / Ignored presets — at least one must render.
        await menuItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var menuItemCount = await menuItems.CountAsync();
        menuItemCount.Should().BeGreaterThan(0, "filter dropdown must render at least one preset filter button");

        // Select the "Grabbed" preset (useHistory.ts FILTERS key 'grabbed',
        // eventType=1). The History list is server-side paged — the preset
        // drives a fresh GET /api/v5/manga/history?...eventType=1. Arm the
        // response wait BEFORE the click: the menu item detaches from the DOM
        // the instant the click registers (the dropdown closes), so awaiting
        // the server-side re-fetch is the durable signal the filter applied.
        var grabbedFilter = menuPortal.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("^Grabbed$", RegexOptions.IgnoreCase) });
        await Page.RunAndWaitForResponseAsync(
            async () => await grabbedFilter.ClickAsync(),
            response => response.Url.Contains("/api/v5/manga/history")
                        && response.Request.Method == "GET");

        // STATE assertion 3 (real server-side filter-state transition): the
        // Grabbed filter drops the seeded row out of the rendered set. The
        // seeded row is a DownloadFailed (eventType=2) row, so filtering on
        // Grabbed (eventType=1) must empty the result set. Combined with the
        // seededCount > 0 assertion above, this proves the filter genuinely
        // changed the rendered row set — not a bare ToBeVisibleAsync shell and
        // not the prior empty-state-tolerant "dropdown opened" check.
        await Assertions.Expect(rowsLocator).ToHaveCountAsync(0, new()
        {
            Timeout = 15_000
        });
        seededCount.Should().BeGreaterThan(
            0,
            "the Grabbed filter row-set change is only meaningful because the unfiltered list had the seeded row");

        // STATE assertion 4: URL preserved across the filter transition (the
        // filter selection transitioned in-place — no spurious navigation).
        Page.Url.Should().EndWith("/manga/activity/history");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — the FKs the raw-SQLite seed helper needs. The chapter set is
    // populated from the MangaDex /feed cassette during the AddManga flow, so at
    // least one chapter exists by the time this runs (polled via SeedFkResolver
    // to absorb the async RefreshMangaCommand chain — GH #277).
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
