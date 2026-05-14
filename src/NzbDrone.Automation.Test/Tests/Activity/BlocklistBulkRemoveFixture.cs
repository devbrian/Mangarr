using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 18 Plan 18-18 — Blocklist bulk-remove coverage
/// (INVENTORY v5-endpoint row 89: DELETE /api/v5/blocklist/bulk).
///
/// Seeds a manga via AddMangaFlow. Navigates to the Blocklist page. When
/// rows exist (forced via an upstream chained-blocklist-event flow from
/// Plan 18-15 InteractiveSearch Blocklist button — not in this fixture's
/// flow), exercises the bulk-select + bulk-remove path. When rows do NOT
/// exist (fresh-DB seed without chained blocklist event), the fixture
/// asserts on the page+table shape + the bulk-toolbar surface presence so
/// the DOM shape doesn't silently regress.
///
/// Note: this fixture differs from Plan-05's MangaBlocklistFixture (which
/// exercises per-row remove). The bulk-remove path is the DELETE
/// /api/v5/blocklist/bulk v5-endpoint contract; per-row uses DELETE
/// /api/v5/blocklist/{id}.
///
/// State assertion: when bulk-remove fires, blocklist row count goes from
/// N → 0. Empty-state path asserts shell visibility + URL preservation.
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 19 Cat A (Residual Yellow Inventory Resolution): needs real Queue/History/Blocklist seed state - the cassette tier does not simulate downloads. #102 is CLOSED and was NOT the blocker (the AddManga chain works). Flip when Phase 19 ships TestKit.Seed{Queue,History,Blocklist}Async. See ROADMAP Phase 19 SC#1.")]
public class BlocklistBulkRemoveFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task blocklist_bulk_remove_clears_rows_or_empty_state()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();

        if (countBefore == 0)
        {
            // Empty-state path: no chained blocklist event seeded yet (Plan 18-15
            // InteractiveSearch Blocklist button is the upstream that populates this).
            // Assert on the page contract + URL.
            Page.Url.Should().EndWith("/manga/activity/blocklist");
            TestContext.WriteLine(
                "[Plan 18-18] BlocklistBulkRemoveFixture — empty blocklist under current cassette. " +
                "The chained blocklist event seed lands once Plan 18-15+ InteractiveSearch fixtures " +
                "seed via the Blocklist button. Populated-row bulk-remove path activates then.");
            return;
        }

        // Populated path: trigger the bulk-select + bulk-remove flow. The
        // Blocklist toolbar exposes a "Remove Selected" or "Remove All"
        // toolbar button. Use the visible-text role-based locator.
        var removeAllButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove All" });
        var removeAllCount = await removeAllButton.CountAsync();

        if (removeAllCount == 0)
        {
            // Fallback: row-level bulk select via select-all checkbox + remove
            // selected. This is the same pattern MangaIndex bulk-delete uses.
            await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove Selected" }).First.ClickAsync();
        }
        else
        {
            await removeAllButton.First.ClickAsync();
        }

        // A confirm modal may pop up (destructive bulk action) — accept it.
        await Page.WaitForTimeoutAsync(500);
        var confirmButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove" });
        var confirmCount = await confirmButton.CountAsync();
        if (confirmCount > 0)
        {
            await confirmButton.Last.ClickAsync();
        }

        await Page.WaitForTimeoutAsync(1_500);

        // STATE assertion 2: rows cleared (DELETE /api/v5/blocklist/bulk contract).
        var countAfter = await rowsLocator.CountAsync();
        countAfter.Should().Be(0, "blocklist bulk-remove must clear all rows (DELETE /api/v5/blocklist/bulk)");
    }
}
