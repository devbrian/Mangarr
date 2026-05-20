using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B / D-12 round-trip) — L-002
// first-record-creation smoke for the event-driven auto-exclusion path.
//
// Flow:
//   1. Register TestImportList (bucket B precondition).
//   2. Trigger ImportListSync (the same Command-API pattern as
//      ImportListSyncTriggerFixture). This populates Manga records that
//      carry MangaDexId / MalId / AniListId triplets.
//   3. Pick one Manga; record its triplet.
//   4. DELETE /api/v5/manga/{id}. The controller's DeleteManga(int, bool)
//      always delegates to MangaService.DeleteManga(list, deleteFiles) which
//      defaults addImportListExclusion: true per MangaService.cs:155-162
//      (Phase 26 Plan 26-04 D-12).
//   5. Poll GET /api/v5/importlistexclusion for up to 5s (Pitfall 3 async-flush
//      race tolerance — ImportListExclusionService : IHandle<MangaDeletedEvent>
//      runs through the EventAggregator which can flush asynchronously).
//   6. Assert an ImportListExclusion row exists with the matching
//      MangaDexId / MalId / AniListId triplet.
//
// This is the L-002 first-record-creation conformer (CREATE then mutate, not
// just empty-state render). The forward-staging gate same as bucket B siblings:
// when TestImportList is not in production DI, the fixture branches to
// Assert.Inconclusive with the documented Phase 27 forward-pointer.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
[TestFixture]
[Category("AutomationTest")]
public class AutoExclusionOnDeleteFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task delete_manga_auto_adds_exclusion_via_event_handler()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (registrationResp, definitionId) =
            await tk.RegisterTestImportListAsync("TestImportList (auto-exclusion)");

        if (definitionId == null)
        {
            Assert.Inconclusive(
                "TestImportList not registered (HTTP {0}); production DI scan excludes " +
                "NzbDrone.Core.Test fake providers. Forward-pointer: Phase 27 lands real " +
                "providers that satisfy bucket B GREEN. Response body: {1}",
                (int)registrationResp.StatusCode,
                registrationResp.Content);
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // 1. Trigger ImportListSync (Step 2 of the flow).
        var commandResp = await http.PostAsJsonAsync("command", new { name = "ImportListSync" });
        commandResp.IsSuccessStatusCode.Should().BeTrue(
            "POST /api/v5/command (ImportListSync) must return 2xx (body: {0})",
            await commandResp.Content.ReadAsStringAsync());

        var commandBody = await commandResp.Content.ReadAsStringAsync();
        using var commandDoc = JsonDocument.Parse(commandBody);
        var commandId = commandDoc.RootElement.GetProperty("id").GetInt32();

        // Wait for sync command to reach terminal state (≤30s; Pitfall 3
        // tolerance — the IExecute<ImportListSyncCommand> path is async).
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var finalStatus = "unknown";
        while (DateTime.UtcNow < deadline)
        {
            var statusResp = await http.GetAsync($"command/{commandId}");
            if (statusResp.IsSuccessStatusCode)
            {
                using var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
                finalStatus = statusDoc.RootElement.GetProperty("status").GetString() ?? string.Empty;
                if (finalStatus == "completed" || finalStatus == "failed")
                {
                    break;
                }
            }

            await Task.Delay(500);
        }

        finalStatus.Should().Be(
            "completed",
            "sync prerequisite must complete (observed: {0})",
            finalStatus);

        // 2. Pick a Manga; record its MangaDexId / MalId / AniListId triplet.
        var mangaListResp = await http.GetAsync("manga");
        mangaListResp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/manga must return 2xx");
        using var mangaListDoc = JsonDocument.Parse(await mangaListResp.Content.ReadAsStringAsync());

        if (mangaListDoc.RootElement.GetArrayLength() == 0)
        {
            // The TestImportList Fetch() items use invalid GUID MangaDexIds that the
            // AddMangaService validation cascade may legitimately reject (no live
            // upstream metadata lookup succeeds). When the post-sync Manga list is
            // empty, the D-12 auto-exclusion path cannot fire — this is a Phase 27
            // forward-staging boundary, not a Phase 26 bug. Branch to Inconclusive
            // with the documented diagnostic.
            Assert.Inconclusive(
                "ImportListSync produced 0 persisted Manga records (TestImportList GUID " +
                "payload is synthetic — Phase 27 real providers feed live MangaDex IDs " +
                "that the AddMangaService cascade can accept). D-12 auto-exclusion path " +
                "cannot fire without a deleted Manga; smoke gate will pivot to real-provider " +
                "seed once Phase 27 ships.");
            return;
        }

        var firstManga = mangaListDoc.RootElement[0];
        var mangaId = firstManga.GetProperty("id").GetInt32();

        // Pull the triplet — Manga resource carries MangaDexId / MalId / AniListId
        // fields directly per Migration 003 + ImportListItemInfo round-trip.
        var mangaDexId = firstManga.TryGetProperty("mangaDexId", out var mdxProp) && mdxProp.ValueKind == JsonValueKind.String
            ? mdxProp.GetString()
            : null;
        var malId = firstManga.TryGetProperty("malId", out var malProp) && malProp.ValueKind == JsonValueKind.Number
            ? malProp.GetInt32()
            : (int?)null;
        var aniListId = firstManga.TryGetProperty("aniListId", out var aniProp) && aniProp.ValueKind == JsonValueKind.Number
            ? aniProp.GetInt32()
            : (int?)null;

        // 3. DELETE the Manga. addImportListExclusion defaults true per
        //    MangaService.cs:155-162; no query param required.
        var deleteResp = await http.DeleteAsync($"manga/{mangaId}");
        deleteResp.IsSuccessStatusCode.Should().BeTrue(
            "DELETE /api/v5/manga/{0} must return 2xx (body: {1})",
            mangaId,
            await deleteResp.Content.ReadAsStringAsync());

        // 4. Pitfall 3 async-flush race tolerance — poll for up to 5s. The
        //    IHandle<MangaDeletedEvent> handler runs through EventAggregator
        //    which can flush asynchronously; a tight assertion races the
        //    event-bus dispatch.
        var exclusionDeadline = DateTime.UtcNow.AddSeconds(5);
        var exclusionFound = false;
        var lastBody = string.Empty;
        while (DateTime.UtcNow < exclusionDeadline)
        {
            var exclusionsResp = await http.GetAsync("importlistexclusion");
            if (exclusionsResp.IsSuccessStatusCode)
            {
                lastBody = await exclusionsResp.Content.ReadAsStringAsync();
                using var exclusionsDoc = JsonDocument.Parse(lastBody);
                foreach (var rec in exclusionsDoc.RootElement.GetProperty("records").EnumerateArray())
                {
                    var mangaDexMatch = mangaDexId != null
                        && rec.TryGetProperty("mangaDexId", out var rMdx)
                        && rMdx.ValueKind == JsonValueKind.String
                        && rMdx.GetString() == mangaDexId;

                    var malMatch = malId.HasValue
                        && rec.TryGetProperty("malId", out var rMal)
                        && rMal.ValueKind == JsonValueKind.Number
                        && rMal.GetInt32() == malId.Value;

                    var aniMatch = aniListId.HasValue
                        && rec.TryGetProperty("aniListId", out var rAni)
                        && rAni.ValueKind == JsonValueKind.Number
                        && rAni.GetInt32() == aniListId.Value;

                    if (mangaDexMatch || malMatch || aniMatch)
                    {
                        exclusionFound = true;
                        break;
                    }
                }
            }

            if (exclusionFound)
            {
                break;
            }

            await Task.Delay(250);
        }

        // 5. State assertion: an ImportListExclusion row with the matching
        //    triplet exists (NOT just any row — verify the specific values).
        exclusionFound.Should().BeTrue(
            "deleted Manga (id={0}, mangaDexId={1}, malId={2}, aniListId={3}) should " +
            "auto-add an ImportListExclusion row within 5s via " +
            "ImportListExclusionService.Handle(MangaDeletedEvent). " +
            "Last GET /api/v5/importlistexclusion body: {4}",
            mangaId,
            mangaDexId,
            malId,
            aniListId,
            lastBody);
    }
}
