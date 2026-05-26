using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 ChapterFile modal sweep) — v5-endpoint axis
/// `DELETE /api/v5/chapterfile/{id}` (INVENTORY row 83).
///
/// Tier (D-04): Nightly default — v5-endpoint write-path (DELETE) per
/// the modal-action-leaning destructive contract.
///
/// **Blocker #4 (deterministic precondition via Plan 20-01
/// SeedChapterFileAsync):** Plan 20-01's raw-SQLite ChapterFile seeder gives
/// every ChapterFile fixture a deterministic precondition row to delete —
/// replaces the prior inconclusive-skip "No ChapterFile present" fallback.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** The MangaDetails Chapters
/// tab has per-row chapter rows but the per-row Delete-File button is not
/// universally wired in v1 (depends on `hasFile` state which requires real
/// import). Per Plan 20-08 ChapterFileDeleted SignalR test + Plan 20-04 /
/// 20-07b / 20-08 / 20-09 path c precedent — ship a Page.APIRequest direct-
/// CRUD fixture asserting on the endpoint contract directly after seeding.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. DELETE /api/v5/chapterfile/{id} returns 2xx for the seeded id.
///   2. Subsequent GET /api/v5/chapterfile/{id} returns 404 (the row is
///      removed; state transition is observable end-to-end).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ChapterFileDeleteFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task delete_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();

        // Blocker #4: consume the Plan 20-01 SeedChapterFileAsync precondition
        // so a ChapterFile row deterministically exists for this fixture to
        // delete. Replaces the prior inconclusive-skip fallback path.
        var testKit = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        var chapterFileId = await testKit.SeedChapterFileAsync(
            Runner.AppData,
            mangaId,
            chapterId,
            "Chapter 1.cbz");

        // Hit DELETE /api/v5/chapterfile/{id} directly (path c). The
        // ChapterFileController auto-derives the route as /api/v5/chapterfile
        // (lower-cased from ResourceName per [V5ApiController]).
        var deleteResp = await Page.APIRequest.DeleteAsync(
            $"{RootUri}/api/v5/chapterfile/{chapterFileId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // STATE assertion 1: DELETE returned 2xx.
        deleteResp.Status.Should().BeInRange(
            200,
            299,
            $"DELETE /api/v5/chapterfile/{chapterFileId} must succeed for the seeded id");

        // STATE assertion 2: subsequent GET returns 404 (the row is removed).
        // This proves the delete is persisted end-to-end — not just modal-close.
        var getResp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/chapterfile/{chapterFileId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        getResp.Status.Should().Be(
            404,
            "after DELETE, GET /api/v5/chapterfile/{id} must return 404 — proves the delete is persisted");
    }

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
