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
/// Phase 18 Plan 18-18 / Phase 19 Plan 19-05 — Blocklist bulk-remove coverage
/// (INVENTORY v5-endpoint row 89: DELETE /api/v5/manga/blocklist/bulk).
///
/// Seeds a manga via AddMangaFlow, then seeds a real <c>MangaBlocklist</c>
/// row via <c>TestKit.SeedBlocklistAsync</c> (Plan 19-01 Open-Question-1
/// verdict = raw-SQLite). A clean chained grab cannot produce a blocklist
/// row (D-01), so the failure-state seed helper is the only honest
/// mechanism. With a blocklist row present, the fixture exercises the
/// bulk-select + bulk-remove path: select-all → "Remove Selected" toolbar
/// button → confirm → DELETE /api/v5/manga/blocklist/bulk drives the
/// blocklist row count N → 0.
///
/// This fixture differs from Plan-05's MangaBlocklistFixture (which exercises
/// per-row remove via DELETE /api/v5/manga/blocklist/{id}). The bulk-remove
/// path is the DELETE /api/v5/manga/blocklist/bulk v5-endpoint contract.
///
/// State assertion: the seeded row count is &gt; 0, and after the bulk
/// DELETE the count-delta is N → 0 — the populated path is the only path
/// (the empty-state branch was deleted in Plan 19-05).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BlocklistBulkRemoveFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task blocklist_bulk_remove_clears_seeded_rows()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Plan 19-05: seed a real MangaBlocklist row via the raw-SQLite
        // TestKit helper (Plan 19-01 verdict). Capture the AddMangaFlow-seeded
        // manga + one of its chapters as the FKs, then INSERT into the
        // backend's per-fixture mangarr.db BEFORE navigating.
        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        // STATE assertion 2: the seed produced at least one blocklist row. A
        // silent seed failure (e.g. a schema drift in MangaBlocklist) fails
        // the test loudly here rather than skipping past an empty page.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();
        countBefore.Should().BeGreaterThan(0, "seed must have produced a blocklist row");

        // Select every row via the table's select-all header checkbox
        // (TableSelectAllHeaderCell renders <input name="selectAll"> inside a
        // CheckInput <label>). The real <input> sits behind a styled overlay
        // <div> that intercepts pointer events, so click the wrapping <label>
        // (CheckInput's onClick handler is on the label). With at least one
        // row selected, the "Remove Selected" toolbar button enables.
        await Page.Locator("label:has(input[name='selectAll'])").First.ClickAsync();

        // Click the "Remove Selected" toolbar button — this opens the
        // destructive-action ConfirmModal (title + confirmLabel "Remove
        // Selected").
        await Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove Selected" })
            .First.ClickAsync();

        // Confirm the destructive bulk DELETE. The ConfirmModal renders
        // role="dialog"; its confirm button carries the "Remove Selected"
        // label. Scope the confirm click to the dialog so it doesn't race the
        // toolbar button that opened it.
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove Selected" })
            .ClickAsync();

        // STATE assertion 3 (count-delta): the bulk DELETE
        // (DELETE /api/v5/manga/blocklist/bulk) cleared every blocklist row.
        // Wait for the first row to detach, then assert the count is 0.
        await rowsLocator.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 30_000
        });
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$")).CountAsync();
        countAfter.Should().Be(
            0,
            "blocklist bulk-remove must clear all rows (DELETE /api/v5/manga/blocklist/bulk)");
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

        var chapterJson = await http.GetStringAsync($"{RootUri}/api/v5/chapter?mangaId={mangaId}");
        using var chapterDoc = JsonDocument.Parse(chapterJson);
        var chapterId = chapterDoc.RootElement[0].GetProperty("id").GetInt32();

        return (mangaId, chapterId);
    }
}
