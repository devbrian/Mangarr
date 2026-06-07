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
/// Phase 20 Plan 20-10 (Wave 3 MangaDetails modal sweep) — v5-endpoint axis
/// `GET /api/v5/chapter` (INVENTORY row 79; canonical path Tests/Manga/ per
/// Blocker #1; reconcile-inventory.py keys row detection on the exact path).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy) maps to PRSmoke
/// per the mechanical row-axis rule.
///
/// MangaDetailsChapterList is the chapter listing surface on the MangaDetails
/// page (`/manga/{slug}` Chapters tab). Loading the page (after AddMangaFlow)
/// fires GET /api/v5/chapter?mangaId={id} via the `useChapters` hook;
/// asserting the wire-shape contract proves the chapter list is observable.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/chapter returns 200 for the seeded manga.
///   2. Response body is a non-empty JSON array (chapter rows).
///   3. Records carry a `mangaId` field matching the seeded manga.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaDetailsChapterListFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task loads_chapter_rows()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        var mangaId = await ResolveMangaIdAsync();

        // GH #277: chapter rows are populated by the async RefreshMangaCommand chain
        // after AddMangaFlow returns. Poll until ≥ 1 row is present before the
        // wire-shape assertion below, so the BeGreaterThan(0) check isn't racing the
        // background refresh on a loaded CI runner.
        await SeedFkResolver.ResolveChapterIdsAsync(RootUri, ApiKey, mangaId, 1);

        // Hit GET /api/v5/chapter?mangaId={id} directly. AddMangaFlow already
        // landed on /manga/{slug} which fires the same endpoint via
        // `useChapters` — this assertion proves the wire-shape contract.
        var response = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/chapter?mangaId={mangaId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // STATE assertion 1: GET /api/v5/chapter returned 200.
        response.Status.Should().Be(
            200,
            "GET /api/v5/chapter must return 200 for the AddMangaFlow-seeded manga");

        // STATE assertion 2: response body is a non-empty JSON array (chapter rows).
        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "chapter listing response must include the chapter rows payload");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "GET /api/v5/chapter response must be a JSON array of ChapterResource");
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(
            0,
            "AddMangaFlow seeds a manga whose chapter list has >= 1 entry (cassette-replayed MangaBaka)");

        // STATE assertion 3: records carry the expected mangaId.
        var firstChapter = doc.RootElement[0];
        firstChapter.GetProperty("mangaId").GetInt32().Should().Be(
            mangaId,
            "every ChapterResource in the listing must carry the seeded mangaId");
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
