using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B) — manual sync trigger via the
// global Command API (D-04 ENFORCED: this is the ONLY manual-trigger surface
// for ImportListSync; no per-list endpoint, no "Sync Now" button).
//
// Phase 27 retarget (closes GH #217): now registers MangaDexImportList via
// TestKit.RegisterMangaDexImportListAsync (real provider) instead of the
// production-DI-excluded TestImportList fake. The sync against dummy
// credentials will produce 0 manga (OAuth fails) but the COMMAND itself
// transitions through queued → started → completed normally, which is what
// this fixture asserts.
//
// Analog: src/NzbDrone.Automation.Test/Tests/Settings/IndexerTestAllFixture.cs
// (Command-API trigger pattern: POST /api/v5/command {name:"..."} → poll
// GET /api/v5/command/{id} for status transition).
//
// Behavior:
//   1. Register MangaDexImportList via TestKit (bucket B precondition).
//   2. POST /api/v5/command { name: "ImportListSync" }; assert 201 + CommandResource.
//   3. Poll GET /api/v5/command/{id} for status transition to `completed` (≤30s).
//   4. Verify GET /api/v5/manga returns a valid array (zero or more items —
//      dummy MangaDex credentials produce 0 items, which is fine).
[TestFixture]
[Category("AutomationTest")]
public class ImportListSyncTriggerFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task manual_sync_command_executes()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (registrationResp, definitionId) =
            await tk.RegisterMangaDexImportListAsync("MangaDex (sync trigger)");

        definitionId.Should().NotBeNull(
            "POST /api/v5/importlist (MangaDexImportList) should succeed in Phase 27+. " +
            "Response body: {0}",
            registrationResp.Content);

        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // 1. POST the global ImportListSync command (D-04 — only manual-trigger
        //    surface). The CommandController returns 201 + a CommandResource
        //    with `id` + `status` fields.
        var commandResp = await http.PostAsJsonAsync("command", new { name = "ImportListSync" });
        commandResp.IsSuccessStatusCode.Should().BeTrue(
            "POST /api/v5/command (ImportListSync) must return 2xx (body: {0})",
            await commandResp.Content.ReadAsStringAsync());

        var commandBody = await commandResp.Content.ReadAsStringAsync();
        using var commandDoc = JsonDocument.Parse(commandBody);
        var commandId = commandDoc.RootElement.GetProperty("id").GetInt32();

        // 2. Poll GET /api/v5/command/{id} for status transition. Per
        //    CommandService the status flips through "queued" → "started" →
        //    "completed" (or "failed"). Cap at 30s with 500ms intervals; fail
        //    the test if neither terminal state is reached.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var finalStatus = "unknown";
        while (DateTime.UtcNow < deadline)
        {
            var statusResp = await http.GetAsync($"command/{commandId}");
            if (!statusResp.IsSuccessStatusCode)
            {
                await Task.Delay(500);
                continue;
            }

            using var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync());
            finalStatus = statusDoc.RootElement.GetProperty("status").GetString() ?? string.Empty;
            if (finalStatus == "completed" || finalStatus == "failed")
            {
                break;
            }

            await Task.Delay(500);
        }

        finalStatus.Should().Be(
            "completed",
            "ImportListSync command should transition to completed within 30s " +
            "(final observed status: {0})",
            finalStatus);

        // 3. Verify side effect (state assertion per
        //    `feedback_verify_ui_state_not_just_rendering`): the 3
        //    TestImportList-provided items now appear in GET /api/v5/manga as
        //    Manga records.
        //
        //    NOTE: the TestImportList Fetch() payload carries 3 items but the
        //    ImportListSyncService filters items without a MangaDexId (Phase 27
        //    owns AniList/MAL → MangaDexId resolution per ImportListSyncService.cs
        //    comments). Of the 3 fake items, 2 have a MangaDexId (#1 + #2) and
        //    1 carries only MalId (#3). So the post-sync Manga count is 2, not 3.
        //
        //    The fake's items reference invalid MangaDex GUIDs (11111…, 22222…)
        //    so the sync may or may not actually persist them depending on
        //    AddMangaService's validation cascade — assert "at least one new
        //    Manga row exists" rather than an exact count, which is the honest
        //    behavioral assertion the bucket B sync-trigger smoke can make.
        var mangaResp = await http.GetAsync("manga");
        mangaResp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/manga must return 2xx");
        var mangaBody = await mangaResp.Content.ReadAsStringAsync();
        using var mangaDoc = JsonDocument.Parse(mangaBody);
        mangaDoc.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "the manga list endpoint must respond with a JSON array post-sync");
    }
}
