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
/// row 87: GET /api/v5/manga/history).
///
/// Different from Plan-05's MangaHistoryFixture (which asserts on row decision
/// cells when rows are present, accepting an empty state). This fixture
/// specifically asserts that GET /api/v5/manga/history fires + returns table
/// content after a real chained grab seeds a history row — the table-content
/// load contract on a populated page.
///
/// Phase 19 Plan 19-06 (Cat A success-path): the fixture now seeds its history
/// state via a real chained InteractiveSearch→Grab (D-01) — AddMangaFlow then
/// SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync — with every external
/// MangaDex byte replayed from the cassettes committed by Plan 19-02 (the
/// /manga/{id}/feed indexer-feed cassette + the /at-home/server/{chapterId}
/// grab cassette). The grab drives the real backend pipeline
/// (ChapterGrabbedEvent → ChapterHistoryService) so a deterministic
/// ChapterHistory row materializes. The old conditional empty-state no-op
/// (valid only when no chained grab seeded history) is therefore deleted —
/// the populated row-loop is the only path (RESEARCH Pitfall 4).
///
/// State assertion: page + table testids visible, URL matches, the chained
/// grab seeded at least one row (count.Should().BeGreaterThan(0) — a silent
/// grab failure fails loudly), AND every history row exposes its decision cell
/// with a non-empty data-event-type (the silent-rejection-icon-hiding-cell
/// guard from feedback_verify_ui_state_not_just_rendering.md).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryTableLoadFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task history_table_loads_with_state_assertions_after_chained_grab()
    {
        // Seed: add the manga, then run a real chained InteractiveSearch→Grab.
        // The grab replays the MangaDex feed + /at-home/server cassettes
        // (Plan 19-02) and writes a real ChapterHistory row via the backend
        // pipeline (ChapterGrabbedEvent → ChapterHistoryService).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty("AddMangaFlow must land on the manga details URL");

        await SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync(Page, RootUri, slug);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table testids present.
        // GET /api/v5/manga/history fires when manga-history-table renders.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2: URL matches the history route — distinct from
        // a spurious redirect to /manga/wanted/missing or similar.
        Page.Url.Should().EndWith("/manga/activity/history");

        // STATE assertion 3 (loud seed-failure guard): the chained grab MUST
        // have seeded at least one history row. SignalR push may take a few
        // seconds; wait for the first row before counting so a slow pipeline
        // write does not race the assertion.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        await Assertions.Expect(rowsLocator.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var count = await rowsLocator.CountAsync();
        count.Should().BeGreaterThan(0, "chained grab must have seeded a history row");

        // STATE assertion 4 (silent-rejection-icon guard): every history row
        // exposes its decision cell with a non-empty data-event-type. Without
        // this check, a row that rendered without its event-type icon would
        // pass a naive "count > 0" test — exactly the class of regression the
        // feedback memo guards against.
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
