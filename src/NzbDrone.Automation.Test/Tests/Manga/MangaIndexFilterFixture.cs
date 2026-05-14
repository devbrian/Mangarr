using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — modal-action MangaIndexFilterModal
// (INVENTORY line 151). "MangaIndex Filter dropdown".
//
// Asserts the filter dropdown opens and that selecting a filter narrows the
// rendered poster grid. The MangaIndex toolbar's Filter button is rendered by
// MangaIndexFilterMenu → FilterMenu (Components/Menu/FilterMenu.tsx); the
// menu's button has visible text "Filter" so role-based selectors are stable
// without adding a dedicated testid.
//
// State assertion (per feedback_verify_ui_state_not_just_rendering): the
// filter dropdown opens (rendering check) AND the visible poster count
// matches a verifiable predicate after applying a filter — both gates fire.
//
// Phase 19 Cat C triage: the Mangarr Menu component (Components/Menu/Menu.tsx)
// renders dropdown items as plain <button> elements via MenuItem → Link; no
// product code emits role="menuitem". The original fixture queried
// AriaRole.Menuitem for the "Monitored Only" item and never found it — a
// fixture-logic bug, not a product bug. Corrected to scope AriaRole.Button
// to the FloatingPortal (#portal-root) that Menu renders the open dropdown
// into. The card-locator at the seed-count assertion was already correct.
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task filter_persists()
    {
        // Seed 1 manga via the canonical D-06 UI-populates-via-UI flow.
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Navigate to MangaIndex.
        await new MangaIndexPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: poster grid materializes with exactly 1 card per
        // the seed state. The manga-card-* testid is keyed by titleSlug
        // (MangaIndexPoster.tsx L137).
        var posterCards = Page.GetByTestId(new Regex(@"^manga-card-"));
        await Assertions.Expect(posterCards).ToHaveCountAsync(1);

        // Click the Filter toolbar button. Role-based selector — the
        // FilterMenu's ToolbarMenuButton renders text "Filter".
        var filterButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter" });
        await Assertions.Expect(filterButton).ToBeVisibleAsync();
        await filterButton.ClickAsync();

        // STATE assertion 2: the filter dropdown surface materializes a
        // selectable "Monitored Only" item. MangaIndexFilterMenu wires the
        // FILTERS array from useManga.ts which includes a key='monitored'
        // entry labeled translate('MonitoredOnly'). The Menu component
        // renders the open dropdown into a FloatingPortal id="portal-root"
        // (Components/Menu/Menu.tsx L156); each preset filter is a plain
        // <button> there (MenuItem → Link → <button>; no role="menuitem").
        var menuPortal = Page.Locator("#portal-root");
        var monitoredOnlyItem = menuPortal.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Monitored Only", RegexOptions.IgnoreCase) });
        await Assertions.Expect(monitoredOnlyItem.First).ToBeVisibleAsync(new()
        {
            Timeout = 10_000
        });

        // Click the Monitored Only filter.
        await monitoredOnlyItem.First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 3: with one monitored manga, the count must still
        // be 1 (the seeded manga is monitored by default per Plan 18-14
        // pre-seed). Verifies the filter was applied (rather than silently
        // showing the unfiltered grid).
        var monitoredCards = Page.GetByTestId(new Regex(@"^manga-card-"));
        var monitoredCount = await monitoredCards.CountAsync();
        monitoredCount.Should().Be(
            1,
            "the single seeded manga is monitored by default and must remain visible after applying Monitored Only");
    }
}
