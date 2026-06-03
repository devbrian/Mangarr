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
/// content on a POPULATED page.
///
/// Phase 39 Plan 39-07 (gap-closure): the prior seed-via-real-grab path
/// (AddMangaFlow → SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync) is
/// structurally dead — the in-process MangaDex/Comix indexers that produced
/// InteractiveSearch release rows were retired in Plan 39-03, so the grab has no
/// release to act on (the sole GatewayIndexer is seeded disabled-by-default). The
/// fixture now seeds its history row directly via TestKit.SeedHistoryFailedAsync
/// (the raw-SQLite seeder — Plan 19-01 verdict; the same mechanism HistoryRetryFixture
/// uses), which writes a DownloadFailed (eventType=2) ChapterHistory row. The
/// populated row-loop remains the only path (RESEARCH Pitfall 4) — the seed makes the
/// page populated WITHOUT depending on the retired in-process search→grab pipeline.
///
/// State assertion: page + table testids visible, URL matches, the seed produced
/// at least one row (count.Should().BeGreaterThan(0) — a silent seed failure fails
/// loudly), AND every history row exposes its decision cell with a non-empty
/// data-event-type (the silent-rejection-icon-hiding-cell guard from
/// feedback_verify_ui_state_not_just_rendering.md).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryTableLoadFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task history_table_loads_with_state_assertions_after_chained_grab()
    {
        // Seed: add the manga, then seed a real DownloadFailed (eventType=2)
        // ChapterHistory row directly via the raw-SQLite TestKit helper so the
        // History page is populated — independent of the retired in-process
        // InteractiveSearch→grab pipeline (Phase 39 Plan 39-07).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty("AddMangaFlow must land on the manga details URL");

        var (mangaId, chapterId) = await SeedFkResolver.ResolveSeedFksAsync(RootUri, ApiKey);
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedHistoryFailedAsync(Runner.AppData, mangaId, chapterId);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table testids present.
        // GET /api/v5/manga/history fires when manga-history-table renders.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2: URL matches the history route — distinct from
        // a spurious redirect to /manga/wanted/missing or similar.
        Page.Url.Should().EndWith("/manga/activity/history");

        // STATE assertion 3 (loud seed-failure guard): the TestKit seed MUST
        // have produced at least one history row. A silent seed failure (e.g. a
        // schema drift in ChapterHistory) fails the test loudly here rather than
        // skipping past an empty page.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        await Assertions.Expect(rowsLocator.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var count = await rowsLocator.CountAsync();
        count.Should().BeGreaterThan(0, "the TestKit seed must have produced a history row");

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
