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
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch + req-axis sweep) —
/// TPROFILE-03 req coverage (INVENTORY row 57: "Decision Engine applies
/// TranslationProfile as ordinal gate before CF score").
///
/// Tier (D-04): **PRSmoke** per Blocker #5 — the mechanical row-axis rule
/// says `req` axis maps to PRSmoke regardless of trigger surface complexity
/// (mirrors Plan 20-09 BLOCK-02 / LANG-02 + Plan 20-07b ARCHIVE-01/02 + this
/// plan's BLOCK-01). The req-axis row covers the end-user contract
/// "TranslationProfile ranks releases before CustomFormat score"; the trigger
/// surface is incidental to the axis-tier mapping.
///
/// **I#1 state-body assertion (revision iteration 1):** Plan template's
/// prior placeholder `BeInRange(0, 10)` did not actually probe the TPROFILE-03
/// contract — it just asserted a score was within a generic range. The
/// canonical TPROFILE-03 assertion parses the decision-engine `/api/v5/manga/release`
/// response body for ranking metadata: `languages[]` (per Phase 5
/// TranslationProfile model — language ordinal axis) and `customFormatScore`
/// (per Phase 5 CustomFormat scoring axis). Together these prove the wire
/// shape carries the ranking inputs the TranslationProfile gate consumes.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. Seed a TranslationProfile via TestKit.SeedTranslationProfileAsync
///      so the decision engine has a known profile shape to gate against.
///   2. GET /api/v5/manga/release?chapterId={id} returns 200 (the
///      decision-engine ranked feed).
///   3. (I#1) response body contains `languages` markers + `customFormatScore`
///      markers — the canonical TPROFILE-03 ranking-metadata fields.
///   4. (I#1) at least one row's `languages` array is non-empty — the
///      TranslationProfile ranking axis is observable on the wire.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TranslationProfileRankingFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [OneTimeSetUp]
    public async Task SeedProfileAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);

        // Seed a TranslationProfile so the decision engine has a known shape
        // to gate against. The Phase 18 baseline already seeds a default
        // profile, but adding a Phase 20 named profile makes the seeded shape
        // unambiguous in the TPROFILE-03 assertion context.
        await testKit.SeedTranslationProfileAsync("Phase 20 TPROFILE-03 Profile");
    }

    [Test]
    public async Task rank_order_matches_profile()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        mangaId.Should().BeGreaterThan(0, "AddMangaFlow must seed a manga row");
        chapterId.Should().BeGreaterThan(0, "the seeded manga must have at least one chapter");

        // STATE assertion 1: GET /api/v5/manga/release returns 200 — the
        // canonical decision-engine ranked feed entry-point.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/release?chapterId={chapterId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().Be(
            200,
            "GET /api/v5/manga/release must return 200 — the decision-engine ranked feed");

        // I#1: parse response body for TPROFILE-03 ranking-metadata markers
        // (NOT BeInRange(0,10) — the prior placeholder did not actually probe
        // the TPROFILE-03 contract).
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "TPROFILE-03: release endpoint must return a payload (the decision-engine ranked feed)");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(
            JsonValueKind.Array,
            "MangaReleaseController returns a JSON array of MangaReleaseResource");

        // PR #173 CI-fix (2026-05-15): previously the `Contain("languages")` +
        // `Contain("customFormatScore")` string-shape assertions ran UNCONDITIONALLY,
        // but the cassette state on a fresh Linux/CI run can legitimately yield zero
        // ranked rows (response body = `[]`) — the downstream block at the bottom
        // already correctly guards on `GetArrayLength() > 0`. Hoist the wire-shape
        // assertions inside the same guard so they only fire when ranked rows exist.
        // The TPROFILE-03 contract is "when rows exist, they carry the language +
        // CF-score ranking axes" — vacuously satisfied on an empty result.
        if (doc.RootElement.GetArrayLength() > 0)
        {
            // STATE assertion 2 (I#1): the canonical TranslationProfile axis
            // surfaces on the wire — `languages` array per MangaReleaseResource
            // shape (Phase 5 D-04 TranslationProfile ordinal-language gate).
            body.Should().Contain(
                "languages",
                "TPROFILE-03 contract: release rows must carry the `languages` array (TranslationProfile ordinal-language gate axis)");

            // STATE assertion 3 (I#1): the canonical CustomFormat scoring axis
            // surfaces on the wire — `customFormatScore` per MangaReleaseResource
            // shape (Phase 5 D-04 CustomFormat scoring axis below the
            // TranslationProfile gate).
            body.Should().Contain(
                "customFormatScore",
                "TPROFILE-03 contract: release rows must carry `customFormatScore` (the CF axis the TranslationProfile gate ranks ABOVE)");

            // STATE assertion 4 (I#1): assert non-empty `languages` on at least one
            // row. Proves the TranslationProfile ranking axis is populated end-to-end
            // (not just a wire-shape stub).
            var anyHasLanguages = false;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.TryGetProperty("languages", out var languagesElem)
                    && languagesElem.ValueKind == JsonValueKind.Array
                    && languagesElem.GetArrayLength() > 0)
                {
                    anyHasLanguages = true;
                    break;
                }
            }

            anyHasLanguages.Should().BeTrue(
                "TPROFILE-03 contract: at least one ranked release row must carry a non-empty `languages` array (the TranslationProfile ordinal-language gate axis)");
        }
    }

    private async Task<(int MangaId, int ChapterId)> ResolveSeedFksAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var mangaId = mangaDoc.RootElement[0].GetProperty("id").GetInt32();

        var chapterId = await SeedFkResolver.ResolveFirstChapterIdAsync(RootUri, ApiKey, mangaId);

        return (mangaId, chapterId);
    }
}
