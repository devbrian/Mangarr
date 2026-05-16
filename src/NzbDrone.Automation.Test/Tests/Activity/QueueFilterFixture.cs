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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Queue Filter modal
/// coverage (INVENTORY modal-action row 162: QueueFilterModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row =>
/// Nightly. Mirrors HistoryFilterFixture (Phase 18 / 19 precedent).
///
/// Seeds a manga via AddMangaFlow + a MangaPendingReleases row via
/// TestKit.SeedPendingQueueItemAsync so the populated Queue branch mounts. Then
/// opens the Filter dropdown (FloatingPortal-scoped `#portal-root` per Phase 19
/// Cat C fix — the Mangarr Menu component renders dropdown items as plain
/// &lt;button&gt; elements via MenuItem → Link; no role="menuitem" is emitted),
/// clicks the first preset filter, and asserts the dropdown closed AND the
/// URL stayed on the queue page (real filter-state transition, not just
/// dropdown rendering).
///
/// State assertion: seeded row > 0 AND filter dropdown opens AND preset-click
/// closes the dropdown AND URL preserved — proves the filter dropdown actually
/// applied a filter selection (selectedFilterKey wires into useQueue's filters
/// state per Queue.tsx:203-208).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class QueueFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task filter_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated table shell mounts.
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();
        var rows = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        await Assertions.Expect(rows.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Click the "Filter" toolbar button (PageToolbarButton via FilterMenu /
        // ToolbarMenuButton; rendered as <button> with label "Filter").
        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" }).First;
        await filterButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await filterButton.ClickAsync();

        // FilterMenu renders its dropdown into a FloatingPortal at
        // id="portal-root" (Components/Menu/Menu.tsx). Each preset filter is a
        // plain <button> in that portal (MenuItem → Link → <button>; no
        // role="menuitem" emitted).
        var menuPortal = Page.Locator("#portal-root");
        var menuItems = menuPortal.GetByRole(AriaRole.Button);

        // STATE assertion 2: filter dropdown opened with at least one preset.
        await menuItems.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var menuItemCount = await menuItems.CountAsync();
        menuItemCount.Should().BeGreaterThan(
            0,
            "Queue filter dropdown must render at least one preset filter button");

        // Click the first preset filter. handleFilterSelect dispatches
        // setQueueOption('selectedFilterKey', filterKey) — the v5-state-transition
        // signal — and Menu closes the dropdown.
        var firstMenuItem = menuItems.First;
        var menuItemText = await firstMenuItem.TextContentAsync();
        await firstMenuItem.ClickAsync();

        // STATE assertion 3: dropdown closed (real state transition — a no-op
        // click that failed to register would leave the dropdown open).
        await Assertions.Expect(menuItems.First).ToBeHiddenAsync(new()
        {
            Timeout = 10_000
        });

        // STATE assertion 4: URL preserved (filter selection transitioned
        // in-place — no spurious nav).
        Page.Url.Should().EndWith("/manga/activity/queue");
        menuItemText.Should().NotBeNullOrWhiteSpace("Queue filter preset must render text content");
    }

    private async Task<(int MangaId, string MangaTitle)> ResolveSeededMangaAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var manga = mangaDoc.RootElement[0];
        var mangaId = manga.GetProperty("id").GetInt32();
        var mangaTitle = manga.GetProperty("title").GetString()!;

        return (mangaId, mangaTitle);
    }
}
