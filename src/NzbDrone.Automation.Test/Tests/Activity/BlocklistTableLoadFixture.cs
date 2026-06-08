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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Blocklist table-load
/// coverage (INVENTORY v5-endpoint row 88: GET /api/v5/manga/blocklist).
///
/// Tier (D-04): PRSmoke per the axis-based heuristic — `v5-endpoint` GET-heavy
/// row. Mirrors HistoryTableLoadFixture (Phase 19 D-04 precedent).
///
/// Seeds a manga via AddMangaFlow, then seeds a real MangaBlocklist row via
/// TestKit.SeedBlocklistAsync (Plan 19-01 raw-SQLite verdict). The seeded row
/// surfaces in GET /api/v5/manga/blocklist.
///
/// State assertion: GET /api/v5/manga/blocklist returns 2xx AND at least one
/// `manga-blocklist-row-{id}` testid renders — the populated path is the
/// only path (no inconclusive-skip per Blocker #4).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class BlocklistTableLoadFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task blocklist_loads()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        var blocklistTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/blocklist") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        var resp = await blocklistTask;

        // STATE assertion 1: v5-endpoint responds 2xx.
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/blocklist must return 2xx with the seeded row");

        // STATE assertion 2: populated table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        // STATE assertion 3: at least one blocklist row renders.
        var rows = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var rowCount = await rows.CountAsync();
        rowCount.Should().BeGreaterThan(
            0,
            "seed must produce a blocklist row visible to the V5 projection");
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
