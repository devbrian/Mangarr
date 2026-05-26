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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Queue bulk-remove
/// coverage (INVENTORY v5-endpoint row 92: DELETE /api/v5/manga/queue/bulk).
///
/// Tier (D-04): Nightly — write-path bulk DELETE leans to modal-action axis.
///
/// Seeds a manga + a MangaPendingReleases pending row via
/// SeedPendingQueueItemAsync (Plan 19-01 D-03 verdict). Then exercises the
/// Queue toolbar select-all + Remove Selected flow: select-all header → Remove
/// Selected toolbar button → RemoveQueueItem ConfirmModal → confirm → bulk
/// DELETE /api/v5/manga/queue/bulk drives the queue row count to 0.
///
/// Distinct from RemoveQueueItemModalFixture (single-row Remove); this is
/// the bulk-DELETE v5-endpoint contract.
///
/// State assertion: seeded row > 0, confirm dispatches bulk DELETE, row count
/// transitions N → 0.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class QueueBulkRemoveFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_remove()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();
        countBefore.Should().BeGreaterThan(
            0,
            "D-03 seed must have produced a queue row before bulk-remove");

        // Select every row via the table's select-all header checkbox
        // (TableSelectAllHeaderCell renders <input name="selectAll"> inside a
        // CheckInput <label>). The real <input> sits behind a styled overlay
        // <div> that intercepts pointer events, so click the wrapping <label>
        // (mirrors BlocklistBulkRemoveFixture / Phase 19 Plan 19-05).
        await Page.Locator("label:has(input[name='selectAll'])").First.ClickAsync();

        // Click the "Remove Selected" toolbar button — opens the
        // RemoveQueueItem ConfirmModal (the same modal as per-row Remove).
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove Selected" })
            .First.ClickAsync();

        // Confirm the bulk DELETE. The modal renders role="dialog"; the
        // confirm button carries the canonical "Remove" label.
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).Last
            .ClickAsync();

        // STATE assertion 2: the bulk DELETE
        // (DELETE /api/v5/manga/queue/bulk) cleared every queue row.
        await rowsLocator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$")).CountAsync();
        countAfter.Should().Be(
            0,
            "queue bulk-remove must clear all rows (DELETE /api/v5/manga/queue/bulk)");
    }

    private async Task<(int MangaId, string MangaTitle)> ResolveSeededMangaAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var manga = mangaDoc.RootElement[0];
        var mangaId = manga.GetProperty("id").GetInt32();
        var mangaTitle = manga.GetProperty("title").GetString()!;

        return (mangaId, mangaTitle);
    }
}
