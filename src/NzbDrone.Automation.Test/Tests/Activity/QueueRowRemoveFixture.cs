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
/// Phase 18 Plan 18-18 / Phase 19 Plan 19-05 — Queue row Remove coverage
/// (INVENTORY v5-endpoint row 91: DELETE /api/v5/manga/queue/{id}).
///
/// Seeds a manga via AddMangaFlow, then seeds a real <c>MangaPendingReleases</c>
/// row via <c>TestKit.SeedPendingQueueItemAsync</c> (Plan 19-01
/// Open-Question-1 verdict = raw-SQLite). This is the D-03 queue seam: the
/// in-memory queue list is structurally dead at runtime (its rebuild event is
/// never published in production), so the <c>MangaPendingReleases</c> table
/// is the only live queue source. The seeded pending row (Reason = Delay)
/// surfaces in the <c>GET /api/v5/manga/queue</c> projection's pending half.
///
/// With a queue row present, the fixture clicks the row's Remove button →
/// RemoveQueueItemModal → confirm → DELETE /api/v5/manga/queue/{id} routes
/// the pending-source ID to RemovePendingQueueItems → the row detaches AND
/// the queue row count decrements N → N-1.
///
/// State assertion: the seeded row count is &gt; 0, the specific removed
/// row detaches, AND the total count decrements — the real
/// DELETE /api/v5/manga/queue/{id} contract is exercised end-to-end (D-03 —
/// "accept Queue empty-state" is explicitly REJECTED; the empty-state
/// skip-branch was deleted in Plan 19-05, the populated path is the only
/// path).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class QueueRowRemoveFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task queue_row_remove_decrements_count_for_seeded_pending_row()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Plan 19-05: seed a real MangaPendingReleases row via the raw-SQLite
        // TestKit helper (Plan 19-01 verdict — D-03 queue seam). Capture the
        // AddMangaFlow-seeded manga id + title, then INSERT into the backend's
        // per-fixture mangarr.db BEFORE navigating. SeedPendingQueueItemAsync
        // also triggers the static _pendingReleases cache rebuild so the row
        // surfaces in GET /api/v5/manga/queue.
        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-queue-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();

        // STATE assertion 2: the seed produced at least one queue row. A
        // silent seed failure (e.g. a schema drift in MangaPendingReleases, or
        // the static-cache rebuild not firing) fails the test loudly here
        // rather than skipping past an empty page.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();
        countBefore.Should().BeGreaterThan(
            0,
            "D-03 seed must have produced a MangaPendingReleases queue row");

        // Populated path: click the per-row Remove button. QueueRow.tsx
        // exposes the SpinnerIconButton with title='RemoveFromQueue' ("Remove
        // from queue") — locate by accessible name substring 'Remove'.
        var firstRow = rowsLocator.First;
        var firstRowIdAttr = await firstRow.GetAttributeAsync("data-testid");
        var firstRowId = firstRowIdAttr!.Replace("manga-queue-row-", string.Empty);

        var removeButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).First;
        await removeButton.ClickAsync();

        // RemoveQueueItemModal opens. Wait for the dialog to be VISIBLE (not
        // just attached) so the Modal-modalBackdrop transition has settled —
        // clicking mid-animation lets the backdrop intercept the pointer
        // event (the failure mode MangaIndexBulkActionsFixture documents).
        // Then scope the confirm click to the dialog itself.
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var confirmButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).Last;
        await confirmButton.ClickAsync();

        // Wait for the specific row to detach rather than sleep.
        var doomedRow = Page.GetByTestId($"manga-queue-row-{firstRowId}");
        await doomedRow.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });

        // STATE assertion 3: the specific row went away (the per-row testid
        // is detached — the DELETE /api/v5/manga/queue/{id} contract routed
        // the pending-source ID to RemovePendingQueueItems).
        var doomedCount = await doomedRow.CountAsync();
        doomedCount.Should().Be(0, "removed queue row must be absent after DELETE /api/v5/manga/queue/{id}");

        // STATE assertion 4: total count decremented (defends against the
        // failure mode where the removed row testid is detached but the row
        // count is stale).
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$")).CountAsync();
        countAfter.Should().Be(countBefore - 1);
    }

    // Resolve the AddMangaFlow-seeded manga id + title via the V5 API — the
    // id is the FK and the title builds the canonical-shaped ParsedChapterInfo
    // the raw-SQLite SeedPendingQueueItemAsync helper persists.
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
