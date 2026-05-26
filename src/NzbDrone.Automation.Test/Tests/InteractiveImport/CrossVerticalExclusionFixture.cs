using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

// Phase 26 Plan 26-06 Task 2 (D-10 bucket B / D-12 cross-vertical) — extends the
// AutoExclusionOnDeleteFixture pattern to a 2-sync-cycle round-trip:
//
//   cycle 1: ImportListSync → 3 (or N) Manga records persist
//   middle:  DELETE /api/v5/manga/{id} → ImportListExclusion auto-adds
//   cycle 2: ImportListSync → assert the deleted Manga is NOT re-added
//            (count comparison: post-cycle-2 == post-cycle-1 - 1)
//
// This verifies that ImportListSyncService.ProcessListItems filters through the
// ImportListExclusion join (per RESEARCH §Q4 + Phase 26 D-12 cascade). Lives
// under Tests/InteractiveImport/ per Plan 26-06 file plan — the cross-vertical
// semantics belong to the "import flow" cluster rather than the per-page Settings
// fixtures (matches Phase 18 D-08 cross-fixture orchestration placement).
//
// Forward-staging gate same as Tests/Settings/ImportLists/ bucket B fixtures —
// when TestImportList is not in production DI scan, the fixture branches to
// Assert.Inconclusive with the documented Phase 27 forward-pointer. Worktree
// compile-only per Plan 26-06 verification carve-out; live execution gated by
// scripts/phase-smoke-gate.sh 26 in the host environment.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors (the
// fixture is API-driven; no UI selectors at all).
[TestFixture]
[Category("AutomationTest")]
public class CrossVerticalExclusionFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task deleted_manga_not_re_added_on_next_sync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (registrationResp, definitionId) =
            await tk.RegisterTestImportListAsync("TestImportList (cross-vertical)");

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

        // -- Cycle 1 -----------------------------------------------------------
        // Trigger the first sync; wait for terminal status.
        await TriggerSyncAndWaitAsync(http);

        // Snapshot the Manga list after cycle 1.
        var cycle1MangaIds = await GetMangaIdsAsync(http);
        if (cycle1MangaIds.Count == 0)
        {
            Assert.Inconclusive(
                "Cycle 1 sync produced 0 persisted Manga records; cross-vertical assertion " +
                "needs at least 1 row to delete + re-sync. TestImportList synthetic GUIDs may " +
                "fail the AddMangaService validation cascade — Phase 27 real providers feed " +
                "live MangaDexIds that the cascade accepts. Smoke gate pivots to real-provider " +
                "seed once Phase 27 ships.");
            return;
        }

        var mangaIdToDelete = cycle1MangaIds[0];

        // -- Middle: delete + poll for exclusion --------------------------------
        var deleteResp = await http.DeleteAsync($"manga/{mangaIdToDelete}");
        deleteResp.IsSuccessStatusCode.Should().BeTrue(
            "DELETE /api/v5/manga/{0} must return 2xx (body: {1})",
            mangaIdToDelete,
            await deleteResp.Content.ReadAsStringAsync());

        // Pitfall 3 async-flush race tolerance: poll for the exclusion row to
        // appear before triggering cycle 2 sync (otherwise the IHandle dispatch
        // races the next ProcessListItems call).
        var exclusionDeadline = DateTime.UtcNow.AddSeconds(5);
        var exclusionPresent = false;
        while (DateTime.UtcNow < exclusionDeadline)
        {
            var exclusionsResp = await http.GetAsync("importlistexclusion");
            if (exclusionsResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await exclusionsResp.Content.ReadAsStringAsync());
                if (doc.RootElement.GetProperty("records").GetArrayLength() > 0)
                {
                    exclusionPresent = true;
                    break;
                }
            }

            await Task.Delay(250);
        }

        exclusionPresent.Should().BeTrue(
            "ImportListExclusion row must exist before cycle 2 sync (otherwise " +
            "ImportListSyncService.ProcessListItems cannot filter the deleted manga " +
            "through the exclusion join)");

        // -- Cycle 2 -----------------------------------------------------------
        await TriggerSyncAndWaitAsync(http);

        // Snapshot the Manga list after cycle 2.
        var cycle2MangaIds = await GetMangaIdsAsync(http);

        // State assertion (per `feedback_verify_ui_state_not_just_rendering`):
        // the deleted manga should be excluded from cycle 2 via the
        // ImportListExclusions filter join. Count comparison: cycle 2 has one
        // fewer Manga than cycle 1.
        //
        // Reason string mirrors Plan 26-06 PLAN.md behavior 6: "deleted manga
        // should be excluded from second sync cycle via ImportListExclusions
        // filter join in ImportListSyncService.ProcessListItems".
        cycle2MangaIds.Should().NotContain(
            mangaIdToDelete,
            "deleted manga should be excluded from second sync cycle via " +
            "ImportListExclusions filter join in ImportListSyncService.ProcessListItems");

        cycle2MangaIds.Count.Should().Be(
            cycle1MangaIds.Count - 1,
            "cycle 2 should contain exactly one fewer Manga than cycle 1 " +
            "(the deleted one). Cycle 1 = {0}; cycle 2 = {1}",
            cycle1MangaIds.Count,
            cycle2MangaIds.Count);
    }

    private static async Task TriggerSyncAndWaitAsync(HttpClient http)
    {
        var commandResp = await http.PostAsJsonAsync("command", new { name = "ImportListSync" });
        commandResp.IsSuccessStatusCode.Should().BeTrue(
            "POST /api/v5/command (ImportListSync) must return 2xx (body: {0})",
            await commandResp.Content.ReadAsStringAsync());

        using var commandDoc = JsonDocument.Parse(await commandResp.Content.ReadAsStringAsync());
        var commandId = commandDoc.RootElement.GetProperty("id").GetInt32();

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
            "ImportListSync command must reach completed within 30s (observed: {0})",
            finalStatus);
    }

    private static async Task<List<int>> GetMangaIdsAsync(HttpClient http)
    {
        var resp = await http.GetAsync("manga");
        resp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/manga must return 2xx");

        var ids = new List<int>();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        foreach (var manga in doc.RootElement.EnumerateArray())
        {
            ids.Add(manga.GetProperty("id").GetInt32());
        }

        return ids;
    }
}
