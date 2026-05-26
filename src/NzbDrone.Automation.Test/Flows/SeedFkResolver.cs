using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace NzbDrone.Automation.Test.Flows;

/// <summary>
/// GH #277 — shared bounded-poll resolver for seed foreign keys (manga id +
/// chapter id(s)) read back via the V5 API after
/// <see cref="AddMangaFlow.AddByMangaDexIdAsync"/>.
///
/// Chapter rows are populated by the ASYNC RefreshMangaCommand chain
/// (<c>MangaAddedEvent → RefreshMangaCommand → chapter-info sync → MangaScannedEvent</c>)
/// which runs in the background AFTER AddMangaFlow returns — the flow only waits
/// for the manga card to render, not for the refresh to finish. On a loaded CI
/// runner the Chapter table can still be empty when a fixture reads
/// <c>GET /api/v5/chapter?mangaId={id}</c>, so a bare <c>RootElement[0]</c>
/// indexes an empty array and throws a <see cref="System.Text.Json"/> array-index
/// error (the pre-existing suite-wide flake GH #277 documents; first observed on
/// run 26470087066, BlocklistTableLoadFixture).
///
/// This helper POLLS the chapter listing until at least the requested number of
/// rows is present (bounded budget) before reading the id(s). The bound expires
/// LOUDLY — a genuine "manga has zero chapters" regression still fails with a
/// clear message rather than being masked. The manga read is the synchronous
/// (non-racy) one, but it is polled identically for uniformity + robustness.
/// </summary>
public static class SeedFkResolver
{
    // ~30s overall poll budget. Matches the GH #277 "10–30s budget" guidance and
    // the 30s WaitForResponse timeouts used elsewhere in the suite. The loop is
    // bounded by BOTH a wall-clock deadline (PollBudget) AND a per-request timeout
    // (RequestTimeout, applied via HttpClient.Timeout in CreateClient) so a single
    // hung request can't blow past the budget — without the per-request bound the
    // default HttpClient.Timeout (100s) would let one stalled call exceed it.
    private const int DelayMs = 500;
    private static readonly TimeSpan PollBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Resolve the first seeded manga id and its first chapter id, polling the
    /// chapter listing until the async refresh has populated ≥ 1 chapter row.
    /// </summary>
    public static async Task<(int MangaId, int ChapterId)> ResolveSeedFksAsync(string rootUri, string apiKey)
    {
        var mangaId = await ResolveFirstMangaIdAsync(rootUri, apiKey);
        var chapterIds = await ResolveChapterIdsAsync(rootUri, apiKey, mangaId, 1);
        return (mangaId, chapterIds[0]);
    }

    /// <summary>
    /// Poll <c>GET /api/v5/manga</c> until ≥ 1 row, then return the first row's id.
    /// </summary>
    public static async Task<int> ResolveFirstMangaIdAsync(string rootUri, string apiKey)
    {
        using var http = CreateClient(apiKey);

        var deadline = DateTime.UtcNow + PollBudget;
        while (true)
        {
            var json = await http.GetStringAsync($"{rootUri}/api/v5/manga");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return doc.RootElement[0].GetProperty("id").GetInt32();
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(DelayMs);
        }

        throw new InvalidOperationException(
            $"SeedFkResolver: GET /api/v5/manga returned an empty array after " +
            $"{PollBudget.TotalSeconds:0}s — AddMangaFlow seed did not persist a manga row.");
    }

    /// <summary>
    /// Poll <c>GET /api/v5/chapter?mangaId={mangaId}</c> until at least
    /// <paramref name="minCount"/> rows are present, then return all chapter ids
    /// in listing order. Throws with a diagnostic message if the async refresh
    /// has not populated the rows within the budget (GH #277).
    /// </summary>
    public static async Task<IReadOnlyList<int>> ResolveChapterIdsAsync(
        string rootUri, string apiKey, int mangaId, int minCount = 1)
    {
        using var http = CreateClient(apiKey);

        var deadline = DateTime.UtcNow + PollBudget;
        while (true)
        {
            var json = await http.GetStringAsync($"{rootUri}/api/v5/chapter?mangaId={mangaId}");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() >= minCount)
            {
                var ids = new List<int>(doc.RootElement.GetArrayLength());
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    ids.Add(element.GetProperty("id").GetInt32());
                }

                return ids;
            }

            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(DelayMs);
        }

        throw new InvalidOperationException(
            $"SeedFkResolver: chapter refresh did not populate ≥ {minCount} row(s) within " +
            $"{PollBudget.TotalSeconds:0}s for mangaId {mangaId} — the async RefreshMangaCommand " +
            "chain (MangaAddedEvent → RefreshMangaCommand → chapter-info sync) had not finished. See GH #277.");
    }

    /// <summary>
    /// Resolve the first chapter id of <paramref name="mangaId"/>, polling until
    /// ≥ 1 chapter row is present (GH #277).
    /// </summary>
    public static async Task<int> ResolveFirstChapterIdAsync(string rootUri, string apiKey, int mangaId)
    {
        var ids = await ResolveChapterIdsAsync(rootUri, apiKey, mangaId, 1);
        return ids[0];
    }

    private static HttpClient CreateClient(string apiKey)
    {
        // Bound each request so a hung call can't exceed the overall poll budget —
        // the default HttpClient.Timeout (100s) would let one stalled request blow
        // far past the ~30s budget. GetStringAsync throws TaskCanceledException on
        // timeout, surfacing a genuinely unresponsive host as a loud test failure.
        var http = new HttpClient { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return http;
    }
}
