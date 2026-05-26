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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Queue table-load coverage
/// (INVENTORY v5-endpoint row 90: GET /api/v5/manga/queue).
///
/// Tier (D-04): PRSmoke per the axis-based heuristic — `v5-endpoint` GET-heavy
/// row maps to PRSmoke. Mirrors HistoryTableLoadFixture (Phase 19 D-04 precedent).
///
/// Seeds a manga via AddMangaFlow, then seeds a real MangaPendingReleases row
/// via TestKit.SeedPendingQueueItemAsync (Plan 19-01 raw-SQLite verdict — D-03
/// queue seam: the in-memory queue rebuild event is never published in
/// production, so the MangaPendingReleases table is the only live queue source).
/// The seeded pending row surfaces in GET /api/v5/manga/queue's pending half.
///
/// State assertion: GET /api/v5/manga/queue returns 200 AND at least one
/// `manga-queue-row-{id}` testid renders — the populated path is the only
/// path (the empty-state branch was deleted in Plan 19-05; no inconclusive-skip).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class QueueTableLoadFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task queue_loads()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // D-03 seed: a real MangaPendingReleases row via the raw-SQLite TestKit
        // helper (Plan 19-01 verdict). SeedPendingQueueItemAsync also triggers
        // the _pendingReleases static-cache rebuild so the row surfaces in
        // GET /api/v5/manga/queue.
        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        // Arm response listener BEFORE navigation so we capture the first
        // GET /api/v5/manga/queue regardless of which load fires it.
        var queueTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/queue") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        var resp = await queueTask;

        // STATE assertion 1: the v5-endpoint contract responded 2xx.
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/queue must return 2xx with the seeded pending row");

        // STATE assertion 2: page + table shell mount (the populated branch
        // in Queue.tsx renders these wrappers only when records.length > 0).
        await Assertions.Expect(Page.GetByTestId("manga-queue-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();

        // STATE assertion 3: at least one queue row renders — proves the
        // GET /api/v5/manga/queue projection round-tripped the seeded
        // MangaPendingReleases row.
        var rows = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        await Assertions.Expect(rows.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var rowCount = await rows.CountAsync();
        rowCount.Should().BeGreaterThan(0, "seed must produce a queue row");
    }

    // Resolve the AddMangaFlow-seeded manga id + title via the V5 API. Mirrors
    // QueueRowRemoveFixture.ResolveSeededMangaAsync (Plan 19-05).
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
