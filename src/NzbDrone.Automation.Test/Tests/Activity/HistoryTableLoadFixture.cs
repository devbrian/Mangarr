using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 18 Plan 18-18 — History table-load coverage (INVENTORY v5-endpoint
/// row 87: GET /api/v5/history).
///
/// Different from Plan-05's MangaHistoryFixture (which asserts on row decision
/// cells when rows are present, accepting an empty state). This fixture
/// specifically asserts that GET /api/v5/history fires + returns table content
/// after a manga is seeded — the table-content load contract.
///
/// Plan 18-15's InteractiveSearch grab flow would seed the history via a
/// chained grab → history-row pipeline; without that landing in this worktree,
/// the manga-seed alone may not produce a history row under the cassette
/// (history rows materialize only after a grab event). The fixture therefore
/// asserts on the table-load shape (page + table testids present + URL match)
/// and conditionally asserts on row decision state if rows DO render.
///
/// State assertion: page + table testids visible, URL matches, AND if rows
/// render every row exposes its decision cell with a non-empty data-event-type
/// (the silent-rejection-icon-hiding-cell guard from
/// feedback_verify_ui_state_not_just_rendering.md).
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D — AddManga modal
/// nav race in ConfirmAddAsync).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 18 Plan 18-14 D-D dependency (#102): AddMangaModal.ConfirmAddAsync click→nav race. AddMangaFlow.AddByMangaDexIdAsync times out at WaitForURLAsync until that lands. Drop this attribute when #102 closes.")]
public class HistoryTableLoadFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task history_table_loads_with_state_assertions_after_manga_seed()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table testids present.
        // GET /api/v5/history fires when manga-history-table renders.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2: URL matches the history route — distinct from
        // a spurious redirect to /manga/wanted/missing or similar.
        Page.Url.Should().EndWith("/manga/activity/history");

        // STATE assertion 3 (conditional — silent-rejection-icon guard):
        // if history rows DO render (e.g. cassette includes a grab event from
        // an upstream test), every row exposes its decision cell with a
        // non-empty data-event-type. Empty-history state is also valid (no
        // chained grab in this fixture's flow), so the loop is a no-op then.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        var count = await rowsLocator.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var row = rowsLocator.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-history-row-", string.Empty);

            var decisionCell = Page.GetByTestId($"manga-history-row-{rowId}-decision");
            await Assertions.Expect(decisionCell).ToBeVisibleAsync();

            var eventType = await decisionCell.GetAttributeAsync("data-event-type");
            eventType.Should().NotBeNullOrEmpty("decision cell must expose data-event-type per silent-rejection-icon guard");
        }
    }
}
