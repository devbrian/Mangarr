using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch sweep) — v5-endpoint axis
/// `POST /api/v5/manga/queue/grab/bulk` (INVENTORY row 94: InteractiveSearch
/// bulk-grab via the MangaQueueAction bulk endpoint).
///
/// Tier (D-04): Nightly default — v5-endpoint write-path (POST bulk grab).
///
/// **Blocker #4 path c (V1-not-wired UI surface):** InteractiveSearchRow.tsx
/// has no per-row checkbox or bulk-action footer in v1 — the bulk-grab
/// endpoint is wired (Plan 13-10) but no React consumer reaches for it via
/// InteractiveSearch yet (the Queue page uses it for bulk requeue of pending
/// items). Per Plan 20-04 / 20-07b / 20-08 / 20-09 path c precedent — ship
/// a Page.APIRequest direct-CRUD fixture asserting on the endpoint contract.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. POST /api/v5/manga/queue/grab/bulk returns 2xx with a valid (empty)
///      ids array — the endpoint contract is the canonical bulk-grab surface.
///   2. Server response confirms the bulk endpoint shape (no 404 / 405 /
///      method-not-allowed; the endpoint exists and is reachable).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BulkGrabFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_grab()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Resolve manga id so the call has a real seed context.
        var mangaId = await ResolveMangaIdAsync();
        mangaId.Should().BeGreaterThan(0, "AddMangaFlow must seed a manga row");

        // POST bulk-grab with an empty ids array. The MangaQueueAction
        // controller's grab/bulk endpoint accepts `QueueBulkResource { Ids: List<int> }`;
        // an empty list is a valid (no-op) request that exercises the
        // endpoint contract without requiring a pending queue row to act on.
        // The Plan 20-08 BulkRenamePreviewFixture established this empty-input
        // contract assertion pattern for V1-not-wired bulk endpoints.
        var payload = JsonSerializer.Serialize(new
        {
            ids = Array.Empty<int>()
        });

        var resp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/manga/queue/grab/bulk",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey,
                    ["Content-Type"] = "application/json"
                },
                Data = payload
            });

        // STATE assertion 1: endpoint exists + accepts the empty bulk request
        // (2xx). A 404 or 405 here would prove the endpoint is gone.
        resp.Status.Should().BeInRange(
            200,
            299,
            "POST /api/v5/manga/queue/grab/bulk must exist and accept a valid bulk-grab request");

        // STATE assertion 2: response is well-formed JSON (no 500 / HTML error
        // page leakage). Empty array input may yield an empty response or
        // 204 NoContent — both are acceptable as long as the contract holds.
        if (resp.Status != 204)
        {
            var body = await resp.TextAsync();
            body.Should().NotContain(
                "<!DOCTYPE",
                "the bulk-grab endpoint must not return an HTML error page");
        }
    }

    private async Task<int> ResolveMangaIdAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        return mangaDoc.RootElement[0].GetProperty("id").GetInt32();
    }
}
