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
/// Phase 20 Plan 20-10 (Wave 3 AddManga + MangaDetails modal sweep) —
/// v5-endpoint axis `POST /api/v5/manga/{id}/links` (INVENTORY row 78;
/// canonical path Tests/Manga/ per Blocker #1).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis maps to PRSmoke per the
/// mechanical row-axis rule. The MangaLinks endpoint is a manual cross-source
/// relink (Phase 2 D-23 + Plan 02-10); CrossSourceIdResolver is BYPASSED
/// here — user-supplied IDs are accepted verbatim.
///
/// **Plan 20-01 LiveService Enumeration row #5 status flip (D-10 — UPFRONT
/// pre-enumerated row):**
///   - Previous status: `provisional` (filed in Plan 20-01 via [#167]).
///   - This plan's outcome (per D-09a cassette ATTEMPT discipline):
///     **`cassette-replayed`** — the relink endpoint is BACKEND-ONLY (per
///     MangaLinksController D-23: "manual-only relink; BYPASSES
///     CrossSourceIdResolver, no upstream AniList/MAL HTTP"). The fixture
///     exercises the backend persistence path; no AniList/MAL cassette is
///     needed because the endpoint never hits those services. Plan 20-01
///     INVENTORY row #5 status flipped from `provisional` → `cassette-replayed`.
///   - Outcome C (NEW LiveService candidate surfaced) does NOT apply: row #5
///     was pre-enumerated UPFRONT and this fixture's outcome is fully accounted
///     for by the existing row.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** No "Resolve" button exists
/// in `frontend/src/Manga/` for the external-links surface; the manual relink
/// endpoint is invoked from Settings flows or via direct CLI. Per Plan 20-04 /
/// 20-07b / 20-08 / 20-09 path c precedent — ship a Page.APIRequest direct-CRUD
/// fixture asserting on the endpoint contract directly.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. POST /api/v5/manga/{id}/links returns 2xx for a valid relink.
///   2. Response body carries the updated MangaResource with the new IDs
///      round-tripped (malId + aniListId persisted).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaLinksResolverFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task resolves_anilist_mal()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var mangaId = await ResolveMangaIdAsync();

        // Pick distinct test values for the AniList + MAL cross-source IDs.
        // These are arbitrary but stable per the relink endpoint's "user-
        // supplied verbatim" semantics (D-23: no auto-validation against
        // upstream). The endpoint persists them; subsequent GETs return them.
        const int testAniListId = 99001;
        const int testMalId = 99002;

        var payload = JsonSerializer.Serialize(new
        {
            aniListId = testAniListId,
            malId = testMalId
        });

        var response = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/manga/{mangaId}/links",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey,
                    ["Content-Type"] = "application/json"
                },
                Data = payload
            });

        // STATE assertion 1: POST /api/v5/manga/{id}/links returns 2xx for a
        // valid relink against a seeded manga.
        response.Status.Should().BeInRange(
            200,
            299,
            "POST /api/v5/manga/{id}/links must succeed when invoked with valid AniList/MAL IDs");

        // STATE assertion 2: response body returns the updated MangaResource
        // with both new IDs persisted (the relink wire-shape contract).
        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "relink response must include the updated MangaResource");
        body.Should().Contain(
            testAniListId.ToString(),
            "relink response must echo the new AniList ID (D-23 user-supplied verbatim persistence)");
        body.Should().Contain(
            testMalId.ToString(),
            "relink response must echo the new MAL ID (D-23 user-supplied verbatim persistence)");
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
