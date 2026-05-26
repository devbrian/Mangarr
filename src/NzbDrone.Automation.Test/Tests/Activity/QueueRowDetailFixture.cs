using System.Collections.Generic;
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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Queue row detail
/// coverage (INVENTORY v5-endpoint row 95: GET /api/v5/manga/queue/details).
///
/// Tier (D-04): PRSmoke per the axis-based heuristic — `v5-endpoint` GET-heavy
/// row.
///
/// The queue/details endpoint surfaces the queue projection's expanded detail
/// payload (consumed by QueueDetailsProvider — Phase 13 follow-up repointed
/// the helper from the deleted TV `/queue/details` onto `/manga/queue/details`).
/// On every manga details page mount, the React Query consumers (e.g.
/// useQueueDetailsForSeries) fire GET /api/v5/manga/queue/details so the
/// chapter rows can show queued-download badges.
///
/// State assertion: GET /api/v5/manga/queue/details returns 2xx — the
/// canonical Activity Queue projection endpoint round-trips with a seeded
/// pending row in place. Driven by a Page.APIRequest direct call (Blocker #4
/// path c — no per-row "expand" UI surface exists in Mangarr's Queue.tsx;
/// the endpoint is consumed by background hooks, not a click).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class QueueRowDetailFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task detail_renders()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated Queue table renders (proves the seed
        // produced a row before exercising the detail endpoint).
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();
        var rows = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        await Assertions.Expect(rows.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // STATE assertion 2: the queue/details endpoint round-trips 2xx.
        // Blocker #4 path c: no per-row "expand" UI surface exists; the
        // endpoint is consumed by QueueDetailsProvider's background hooks
        // (Phase 13 follow-up), so exercise it directly via APIRequest.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/queue/details",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/queue/details must return 2xx with a seeded pending row");
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
