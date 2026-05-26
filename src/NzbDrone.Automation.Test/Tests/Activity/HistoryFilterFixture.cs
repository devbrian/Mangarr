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
/// Phase 19 Plan 19-06 (Cat A success-path): seeds history state via a real
/// chained InteractiveSearch→Grab (D-01) — AddMangaFlow then
/// SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync — with every
/// external MangaDex byte replayed from the cassettes committed by Plan 19-02.
/// The grab drives the real backend pipeline (ChapterGrabbedEvent →
/// ChapterHistoryService) so a deterministic Grabbed (eventType=1)
/// ChapterHistory row materializes. With history populated, the filter
/// assertions are tightened: instead of the prior empty-state-tolerant "the
/// filter UI is reachable" check, the fixture now asserts the filter actually
/// changes the rendered manga-history-row-* set — the History filter is
/// server-side paged (selectedFilterKey → /manga/history?eventType=N), so the
/// "Failed" preset (eventType=4) must drop the grabbed row out of the result
/// set while the default "All" preset keeps it.
///
/// Phase 19 Cat C triage (cross-plan, mirrors Plan 19-03's MangaMissingFilter /
/// MangaIndexFilter correction): the Mangarr Menu component
/// (Components/Menu/Menu.tsx) renders dropdown items as plain &lt;button&gt;
/// elements via MenuItem → Link; no product code emits role="menuitem". The
/// original fixture queried AriaRole.Menuitem and always found 0 items — a
/// fixture-logic bug, not a product bug. Corrected to scope AriaRole.Button to
/// the FloatingPortal (#portal-root) that Menu renders the open dropdown into.
///
/// State assertion: the chained grab seeds a Grabbed row (seededCount > 0),
/// the filter dropdown opens (preset filter buttons inside the floating menu
/// portal), and applying the "Failed" preset (eventType=4) empties the
/// rendered manga-history-row-* set — the grabbed row is eventType=1, so the
/// server-side filter genuinely changes the row set. A real filter-state
/// transition, not just the dropdown visually opening.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Phase 19 D-05: Comix cannot be HTTP-cassette'd (Phase 18 D-11 — the
        // runtime signer hits comix.to live). Disable it BEFORE the chained
        // InteractiveSearch so the indexer fan-out is MangaDex-only (the
        // cassette-replayable path). NUnit runs the base AutomationTest
        // [OneTimeSetUp] (boot + baseline seed) before this derived one, so
        // RootUri/ApiKey are wired by the time this runs.
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task history_filter_menu_opens_and_applies_filter()
    {
        // Seed: add the manga, then run a real chained InteractiveSearch→Grab
        // so the History page has a real Grabbed (eventType=1) row to filter.
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty("AddMangaFlow must land on the manga details URL");

        await SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync(Page, RootUri, slug);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present, and the chained grab seeded a row.
        // SignalR push may take a few seconds; wait for the first row.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        await Assertions.Expect(rowsLocator.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var seededCount = await rowsLocator.CountAsync();
        seededCount.Should().BeGreaterThan(0, "chained grab must have seeded a Grabbed history row");

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

        // Select the "Failed" preset (useHistory.ts FILTERS key 'failed',
        // eventType=4). The History list is server-side paged — the preset
        // drives a fresh GET /api/v5/manga/history?...eventType=4. Arm the
        // response wait BEFORE the click: the menu item detaches from the DOM
        // the instant the click registers (the dropdown closes), so awaiting
        // the server-side re-fetch is the durable signal the filter applied.
        var failedFilter = menuPortal.GetByRole(AriaRole.Button,
            new() { NameRegex = new Regex("^Failed$", RegexOptions.IgnoreCase) });
        await Page.RunAndWaitForResponseAsync(
            async () => await failedFilter.ClickAsync(),
            response => response.Url.Contains("/api/v5/manga/history")
                        && response.Request.Method == "GET");

        // STATE assertion 3 (real server-side filter-state transition): the
        // Failed filter drops the grabbed row out of the rendered set. The
        // grabbed chapter is a Grabbed (eventType=1) row, so filtering on
        // Failed (eventType=4) must empty the result set. Combined with the
        // seededCount > 0 assertion above, this proves the filter genuinely
        // changed the rendered row set — not a bare ToBeVisibleAsync shell and
        // not the prior empty-state-tolerant "dropdown opened" check.
        await Assertions.Expect(rowsLocator).ToHaveCountAsync(0, new()
        {
            Timeout = 15_000
        });
        seededCount.Should().BeGreaterThan(
            0,
            "the Failed filter row-set change is only meaningful because the unfiltered list had the grabbed row");

        // STATE assertion 4: URL preserved across the filter transition (the
        // filter selection transitioned in-place — no spurious navigation).
        Page.Url.Should().EndWith("/manga/activity/history");
    }
}
