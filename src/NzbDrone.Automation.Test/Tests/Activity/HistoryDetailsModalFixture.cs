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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — History Details modal
/// coverage (INVENTORY modal-action row 160: HistoryDetailsModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// Seeds a History row via the raw-SQLite SeedHistoryFailedAsync helper
/// (Plan 19-01 verdict). The seeded row is a DownloadFailed event with a
/// canonical `Data` dict (`Message`, `Indexer`, `DownloadClient`, `Source`)
/// that the HistoryDetailsModal renders. Clicking the per-row Details
/// IconButton (aria-label "Details") opens the HistoryDetailsModal; assert
/// the modal renders and exposes the seeded Message content.
///
/// State assertion: seeded row count > 0 → click row Details button → dialog
/// renders → dialog content contains the seeded failure Message (proves the
/// seeded Data JSON column round-tripped through the V5 history projection
/// AND the modal rendered its branch). NOT bare ToBeVisibleAsync.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class HistoryDetailsModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;
    private const string SeededFailureMessage = "TestKit-seeded failed download";

    [Test]
    public async Task detail_renders()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedHistoryFailedAsync(Runner.AppData, mangaId, chapterId);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated history table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "seed must have produced a history row");

        var firstRow = rowsLocator.First;

        // HistoryRow.tsx exposes per-row icon-only IconButton with
        // aria-label="Details" (HistoryRow.tsx:266) → opens HistoryDetailsModal.
        var detailsButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Details" });
        await detailsButton.ClickAsync();

        // STATE assertion 2: the HistoryDetailsModal renders (role=dialog).
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion 3: the modal renders the seeded downloadFailed
        // detail rows. HistoryDetails.tsx's `downloadFailed` branch surfaces
        // the seeded `Data.Message` — its presence proves the seeded row's
        // JSON `Data` column round-tripped through the V5 history projection
        // AND the modal branch actually rendered (not a blank dialog).
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().Contain(
            SeededFailureMessage,
            "HistoryDetailsModal must render the seeded downloadFailed Message");
    }

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
