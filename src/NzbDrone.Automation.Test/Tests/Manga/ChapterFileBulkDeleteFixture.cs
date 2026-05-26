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
/// `DELETE /api/v5/chapterfile/bulk` (INVENTORY row 84).
///
/// Tier (D-04): Nightly default — v5-endpoint write-path (DELETE bulk) per
/// the modal-action-leaning destructive contract.
///
/// **Blocker #4 (deterministic precondition via Plan 20-01
/// SeedChapterFileAsync):** Seeds 2+ ChapterFile rows so the bulk delete has
/// >= 1 entry to act on — replaces the prior inconclusive-skip "No
/// ChapterFiles present" fallback.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** No bulk-delete modal UI
/// is wired on MangaDetails Chapters tab in v1 (the bulk-delete is reached
/// via Activity / system flows). Per Plan 20-04 / 20-07b / 20-08 / 20-09
/// path c precedent — ship a Page.APIRequest direct-CRUD fixture asserting
/// on the endpoint contract directly.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. DELETE /api/v5/chapterfile/bulk returns 2xx for the seeded ids.
///   2. Subsequent GET /api/v5/chapterfile/{id} for each id returns 404
///      (the rows are removed; state transition is observable end-to-end).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ChapterFileBulkDeleteFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task bulk_delete()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapter1Id, chapter2Id) = await ResolveSeedFksAsync();

        // Blocker #4: seed 2 ChapterFile rows so the bulk-delete has multiple
        // entries to act on. Distinct chapter ids so the FK constraint holds.
        var testKit = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        var chapterFile1Id = await testKit.SeedChapterFileAsync(
            Runner.AppData,
            mangaId,
            chapter1Id,
            "Chapter 1.cbz");
        var chapterFile2Id = await testKit.SeedChapterFileAsync(
            Runner.AppData,
            mangaId,
            chapter2Id,
            "Chapter 2.cbz");

        // Bulk delete body shape per `ChapterFileListResource`:
        //   { "chapterFileIds": [id1, id2] }
        var payload = JsonSerializer.Serialize(new
        {
            chapterFileIds = new[] { chapterFile1Id, chapterFile2Id }
        });

        // The Playwright DeleteAsync method does not directly support a JSON
        // body on a DELETE — use the generic FetchAsync with the DELETE method.
        var deleteResp = await Page.APIRequest.FetchAsync(
            $"{RootUri}/api/v5/chapterfile/bulk",
            new APIRequestContextOptions
            {
                Method = "DELETE",
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey,
                    ["Content-Type"] = "application/json"
                },
                Data = payload
            });

        // STATE assertion 1: DELETE returned 2xx (typically 204 NoContent).
        deleteResp.Status.Should().BeInRange(
            200,
            299,
            "DELETE /api/v5/chapterfile/bulk must succeed for the seeded ids");

        // STATE assertion 2: subsequent GETs return 404 for each row.
        foreach (var id in new[] { chapterFile1Id, chapterFile2Id })
        {
            var getResp = await Page.APIRequest.GetAsync(
                $"{RootUri}/api/v5/chapterfile/{id}",
                new APIRequestContextOptions
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Api-Key"] = ApiKey
                    }
                });
            getResp.Status.Should().Be(
                404,
                $"after bulk DELETE, GET /api/v5/chapterfile/{id} must return 404 — proves the delete is persisted");
        }
    }

    private async Task<(int MangaId, int Chapter1Id, int Chapter2Id)> ResolveSeedFksAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var mangaId = mangaDoc.RootElement[0].GetProperty("id").GetInt32();

        var chapterIds = await SeedFkResolver.ResolveChapterIdsAsync(RootUri, ApiKey, mangaId, 2);

        return (mangaId, chapterIds[0], chapterIds[1]);
    }
}
