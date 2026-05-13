using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

// Phase 18 Plan-05: History cluster coverage.
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08) then navigates to
// the History page. Asserts on the decision cell per
// feedback_verify_ui_state_not_just_rendering.md — the
// `manga-history-row-{id}-decision` cell is the one that hides silent rejection
// icons (Phase 2 retro precedent). Each rendered row must expose its decision
// cell with a non-empty `data-event-type` attribute, otherwise the silent-row
// regression has resurfaced.
//
// Cassette-state-dependent — empty History is valid (page + table render but
// the row loop is a no-op).
[TestFixture]
[Category("AutomationTest")]
public class MangaHistoryFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task history_page_renders_table_with_decision_cells_when_rows_present()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2 (silent-rejection-icon-hiding-cell):
        // every visible history row MUST expose its decision cell. Without this
        // check, a row that rendered without its event-type icon would pass a
        // naive "rows.Count > 0" test — exactly the class of regression the
        // feedback memo guards against.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        var count = await rowsLocator.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var row = rowsLocator.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-history-row-", string.Empty);

            var decisionCell = Page.GetByTestId($"manga-history-row-{rowId}-decision");
            await Assertions.Expect(decisionCell).ToBeVisibleAsync();

            // STATE assertion 3 (decision state, not just rendering):
            // the cell exposes its event-type so the silent-empty-icon case is
            // caught. data-event-type is wired in HistoryEventTypeCell.tsx.
            var eventType = await decisionCell.GetAttributeAsync("data-event-type");
            eventType.Should().NotBeNullOrEmpty();
        }

        Page.Url.Should().EndWith("/manga/activity/history");
    }
}
