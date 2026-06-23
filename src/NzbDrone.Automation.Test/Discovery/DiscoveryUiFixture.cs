using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Discovery;

/// <summary>
/// Phase 42 Plan 42-08 — live Playwright E2E for the Discovery vertical (the
/// E2E half of the 42-VALIDATION Nyquist contract, DISC-01/04/07/08 row; run
/// live per the `auto-run-browser-smoke-tests` convention).
///
/// <para>
/// Coverage is split into a DETERMINISTIC core and a LIVE-GATED flow:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Core (unconditional, PRSmoke): the `nav-discovery` sidebar entry
///     navigates to <c>/discovery</c>; the page shows the
///     "Set filters and Search" empty state on first load (no MangaBaka call);
///     opening the Filters right-drawer and clicking a Type tristate chip cycles
///     neutral → include → exclude → neutral (asserted via the chip's
///     <c>data-state</c> attribute). The Type chips are local constants
///     (<c>TYPE_OPTIONS</c>) so this requires no network — it is the
///     deterministic DISC-01/04 proof.
///     </description>
///   </item>
///   <item>
///     <description>
///     Live-gated: clicking Search issues the live <c>POST /api/v5/discovery/search</c>
///     against api.mangabaka.org (eligibility auto-paging loop). When the call
///     is reachable and yields a grid, the fixture exercises Search → grid →
///     "Add N Manga" count-only modal open, plus per-card Exclude → optimistic
///     drop → Undo toast. If MangaBaka is unreachable in the harness (offline
///     CI tier), the live assertions are skipped via <c>Assert.Inconclusive</c>
///     so the deterministic core still gates.
///     </description>
///   </item>
/// </list>
///
/// State-not-rendering (per feedback_verify_ui_state_not_just_rendering): the
/// chip cycle asserts the <c>data-state</c> value transitions (not bare
/// visibility); the live flow asserts the <c>POST /discovery/search</c> status
/// and the post-Exclude grid card-count decrement.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class DiscoveryUiFixture : AutomationTest
{
    private const string DiscoverySearchEndpoint = "/api/v5/discovery/search";

    [Test]
    public async Task discovery_nav_drawer_and_empty_state()
    {
        // (1) The nav-discovery sidebar entry navigates to /discovery.
        await Page.GetByTestId("nav-discovery").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/discovery$"));

        // (2) First load shows the "Set filters and Search" empty state — no
        // results, no MangaBaka call (the page is manual-trigger by design, D-03).
        var emptyState = Page.GetByTestId("discovery-empty-state");
        await Assertions.Expect(emptyState).ToBeVisibleAsync();

        // The grid must NOT be present before a Search (state assertion: the page
        // did not auto-fire a browse).
        await Assertions.Expect(Page.GetByTestId("discovery-grid")).ToHaveCountAsync(0);

        // (3) Open the Filters right-drawer.
        await Page.GetByTestId("discovery-filters-button").ClickAsync();
        var drawer = Page.GetByTestId("discovery-filter-drawer");
        await Assertions.Expect(drawer).ToBeVisibleAsync();

        // (4) Cycle a Type tristate chip neutral -> include -> exclude -> neutral.
        // The Type section is fed local TYPE_OPTIONS constants, so the `manga`
        // chip is present without any remote option-list call.
        var chip = Page.GetByTestId("discovery-chip-manga");
        await Assertions.Expect(chip).ToBeVisibleAsync();

        (await chip.GetAttributeAsync("data-state")).Should().Be(
            "neutral",
            "a fresh Type chip starts neutral");

        await chip.ClickAsync();
        await Assertions.Expect(chip).ToHaveAttributeAsync("data-state", "include");

        await chip.ClickAsync();
        await Assertions.Expect(chip).ToHaveAttributeAsync("data-state", "exclude");

        await chip.ClickAsync();
        await Assertions.Expect(chip).ToHaveAttributeAsync("data-state", "neutral");

        // (5) Close the drawer via its backdrop (the open drawer's full-bleed
        // overlay covers the toolbar, so the Filters toggle button is NOT
        // clickable while open — only the backdrop / X link close it). The
        // fixture's Page is created once in [OneTimeSetUp] and SHARED across the
        // fixture's tests (DB is wiped per-fixture, D-05; the page is not), so
        // leaving the drawer open would block the sibling test's interactions.
        // Also gives the drawer its close-cycle coverage.
        await drawer.GetByLabel("Close").ClickAsync();
        await Assertions.Expect(drawer).ToHaveCountAsync(0);
    }

    [Test]
    public async Task discovery_search_grid_add_modal_and_exclude_undo()
    {
        // Land on /discovery via a fresh load so this test is self-isolating: the
        // fixture's Page is created once in [OneTimeSetUp] and SHARED across the
        // fixture's tests, so a client-side nav from a sibling test's leftover
        // state (e.g. an open FilterDrawer overlay) could block interactions. A
        // full GotoAsync remounts the page with the drawer closed. (Nav-via-sidebar
        // is covered by discovery_nav_drawer_and_empty_state.)
        await Page.GotoAsync($"{RootUri}/discovery");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/discovery$"));
        await Assertions.Expect(Page.GetByTestId("discovery-search-button")).ToBeVisibleAsync();

        // Arm the live search response listener BEFORE pressing Search so the
        // in-flight POST is not missed. A broad (no-filter) browse keeps the
        // eligibility loop's first page small/fast.
        var searchRegex = new Regex(Regex.Escape(DiscoverySearchEndpoint) + @"(\?|$)");

        IResponse searchResponse;
        try
        {
            var responseTask = Page.WaitForResponseAsync(
                r => searchRegex.IsMatch(r.Url) && r.Request.Method == "POST",
                new() { Timeout = 45_000 });

            await Page.GetByTestId("discovery-search-button").ClickAsync();
            searchResponse = await responseTask;
        }
        catch (TimeoutException)
        {
            Assert.Inconclusive(
                "POST /discovery/search did not return within 45s — MangaBaka is "
                + "unreachable in this harness tier; the deterministic nav/drawer/"
                + "empty-state core is covered by discovery_nav_drawer_and_empty_state.");
            return;
        }

        // STATE assertion: the live browse endpoint returned 2xx (admin-authed,
        // proves T-42-04-AUTHN end-to-end with the X-Api-Key context header).
        searchResponse.Status.Should().BeInRange(
            200,
            299,
            "POST /discovery/search is the admin-authed live browse contract");

        // Settle: after the search resolves the page lands on exactly one of the
        // grid / empty-state / pool-exhausted terminal states. Auto-wait on the
        // grid; if no eligible results came back, skip the live curate-loop
        // assertions (still a green deterministic core).
        var grid = Page.GetByTestId("discovery-grid");
        try
        {
            await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            Assert.Inconclusive(
                "Search returned no eligible results (empty grid / pool exhausted) — "
                + "nothing to curate; the live MangaBaka call itself succeeded "
                + $"(status {searchResponse.Status}).");
            return;
        }

        await Assertions.Expect(grid).ToBeVisibleAsync();

        // (5) The Add button enables once results exist; pressing it opens the
        // count-only "Add N Manga" modal.
        var addButton = Page.GetByTestId("discovery-add-button");
        await Assertions.Expect(addButton).ToBeEnabledAsync();
        await addButton.ClickAsync();

        var addModal = Page.GetByTestId("add-top-x-modal");
        await Assertions.Expect(addModal).ToBeVisibleAsync();

        // Close the modal without committing the fire-and-forget bulk-add (the
        // unit tier pins the command enqueue; this E2E proves the modal opens
        // from the live grid).
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(addModal).ToHaveCountAsync(0);

        // (6) Per-card Exclude drops the card and surfaces an Undo toast.
        var cardsBefore = await Page.GetByTestId(new Regex(@"^discovery-card-\d+$")).CountAsync();
        cardsBefore.Should().BeGreaterThan(0, "the grid rendered at least one result card");

        var firstExclude = Page.GetByTestId(new Regex(@"^discovery-card-exclude-\d+$")).First;
        await firstExclude.ClickAsync();

        // Undo toast appears (optimistic exclude wrote the global ImportListExclusion).
        var undoToast = Page.GetByTestId("discovery-undo-toast");
        await Assertions.Expect(undoToast).ToBeVisibleAsync();

        // STATE assertion: the optimistic drop removed exactly the excluded card.
        await Assertions
            .Expect(Page.GetByTestId(new Regex(@"^discovery-card-\d+$")))
            .ToHaveCountAsync(cardsBefore - 1);

        // Undo re-inserts the card (DELETEs the just-written exclusion).
        await Page.GetByTestId("discovery-undo-button").ClickAsync();
        await Assertions
            .Expect(Page.GetByTestId(new Regex(@"^discovery-card-\d+$")))
            .ToHaveCountAsync(cardsBefore);
    }
}
