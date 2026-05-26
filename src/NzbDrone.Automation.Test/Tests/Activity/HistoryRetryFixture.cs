using System.Net;
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
/// Phase 18 Plan 18-18 / Phase 19 Plan 19-05 — History failed-row retry
/// coverage (INVENTORY req row 62, HISTORY-03: User can retry failed
/// download from History row; INVENTORY v5-endpoint
/// POST /api/v5/manga/history/failed/{id}/retry).
///
/// Seeds a manga via AddMangaFlow, then seeds a real <c>DownloadFailed</c>
/// <c>ChapterHistory</c> row via <c>TestKit.SeedHistoryFailedAsync</c> (Plan
/// 19-01 Open-Question-1 verdict = raw-SQLite). A clean chained grab cannot
/// produce a *failed*-download row (D-01), so the failure-state seed helper
/// is the only honest mechanism.
///
/// Plan 19-05 fix-forward (deviation Rule 1): the original fixture assumed
/// HistoryDetailsModal exposes a "Try Again" button for downloadFailed rows.
/// It does NOT — HistoryDetailsModal.tsx only renders the "Mark As Failed"
/// SpinnerButton, and only for `grabbed` rows. The HISTORY-03 retry endpoint
/// (POST failed/{id}/retry) has NO frontend UI surface at all. This fixture
/// therefore exercises the two contracts that genuinely exist for a seeded
/// downloadFailed row:
///   1. UI: the per-row Details IconButton → HistoryDetailsModal renders the
///      downloadFailed details (Name / Message / Indexer) — proving the
///      seeded row's `Data` dict round-tripped through the V5 history
///      projection and the modal.
///   2. API: POST /api/v5/manga/history/failed/{id}/retry returns 204 and
///      enqueues a ChapterSearchCommand — the HISTORY-03 manual-retry
///      contract, exercised end-to-end (it has no UI button to click).
///
/// State assertion: the seeded row count is &gt; 0, its decision cell's
/// event-type is <c>downloadFailed</c>, the modal renders the seeded failure
/// Message, and the retry POST returns 204 with a queued ChapterSearchCommand
/// — the populated path is the only path (the empty-state branch was deleted
/// in Plan 19-05).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryRetryFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;
    private const string SeededFailureMessage = "TestKit-seeded failed download";

    [Test]
    public async Task history_failed_row_renders_details_and_retry_endpoint_enqueues_search()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Plan 19-05: seed a real DownloadFailed ChapterHistory row via the
        // raw-SQLite TestKit helper (Plan 19-01 verdict). Capture the
        // AddMangaFlow-seeded manga + one of its chapters as the FKs, then
        // INSERT into the backend's per-fixture mangarr.db BEFORE navigating.
        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedHistoryFailedAsync(Runner.AppData, mangaId, chapterId);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table testids present.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2: the seed produced at least one history row. A
        // silent seed failure (e.g. a schema drift in ChapterHistory) fails
        // the test loudly here rather than skipping past an empty page.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "seed must have produced a failed history row");

        // The seeded row is the only history row (per-fixture DB, D-05).
        var firstRow = rowsLocator.First;
        var rowIdAttr = await firstRow.GetAttributeAsync("data-testid");
        var rowId = rowIdAttr!.Replace("manga-history-row-", string.Empty);

        // STATE assertion 3 (decision state, not just rendering): the seeded
        // row's decision cell exposes event-type downloadFailed — proving the
        // seed produced a genuinely failed row (EventType = 2, Successful = 0).
        var decisionCell = Page.GetByTestId($"manga-history-row-{rowId}-decision");
        await Assertions.Expect(decisionCell).ToBeVisibleAsync();
        var eventType = await decisionCell.GetAttributeAsync("data-event-type");
        eventType.Should().Be(
            "downloadFailed",
            "the seeded ChapterHistory row is a DownloadFailed event");

        // UI contract: open the per-row Details modal (the only per-row action
        // HistoryRow.tsx exposes — an icon-only IconButton with
        // aria-label="Details").
        var detailsButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Details" });
        await detailsButton.ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // STATE assertion 4: HistoryDetailsModal renders the downloadFailed
        // detail rows. HistoryDetails.tsx's `downloadFailed` branch surfaces
        // the seeded `Data` dict's Message — its presence in the dialog proves
        // the seeded row's JSON `Data` column round-tripped through the V5
        // history projection intact.
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().Contain(
            SeededFailureMessage,
            "HistoryDetailsModal must render the seeded downloadFailed Message");

        // API contract (HISTORY-03): POST failed/{id}/retry. This endpoint
        // has NO frontend UI surface (no "Try Again" button exists), so the
        // manual-retry contract is exercised directly against the V5 API —
        // it must return 204 and enqueue a ChapterSearchCommand.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var retryResponse = await http.PostAsync(
            $"{RootUri}/api/v5/manga/history/failed/{rowId}/retry", null);

        // STATE assertion 5: the retry endpoint accepted the request.
        retryResponse.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "POST failed/{id}/retry must return 204 for a real failed history row");

        // STATE assertion 6: the retry enqueued a ChapterSearchCommand — the
        // HISTORY-03 manual-retry escape hatch. The command queue retains a
        // window of recent commands; any payload referencing the canonical
        // ChapterSearch name proves the retry reached the command queue.
        var commands = await http.GetStringAsync($"{RootUri}/api/v5/command");
        commands.Should().Contain(
            "ChapterSearch",
            "the retry must enqueue a ChapterSearchCommand (HISTORY-03)");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — these are the FKs the raw-SQLite seed helper needs. The
    // chapter set is populated from the MangaDex /feed cassette during the
    // AddManga flow, so at least one chapter exists by the time this runs.
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
