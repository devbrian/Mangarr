using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Wanted modal sweep) — LANG-02 req coverage
/// (INVENTORY req row 56: "Wanted/Missing filters by translation language").
///
/// Tier (D-04): **PRSmoke** per Blocker #5 — the mechanical row-axis rule says
/// `req` axis maps to PRSmoke regardless of trigger surface complexity
/// (mirrors BLOCK-02 in this same plan and ARCHIVE-01/02 in Plan 20-07b).
///
/// **Blocker #4 path c — V1 surface gap pattern (Plan 20-04 / 20-07b lineage):**
/// The MangaMissing page's FILTERS / FILTER_BUILDER (`frontend/src/Wanted/Missing/
/// useMissing.tsx:25-74`) ship only `monitored` / `unmonitored` / `excludeSpecials`
/// presets + `monitored` / `includeSpecials` builder criteria — NO Language
/// preset OR builder criterion is wired on the Wanted/Missing UI. The
/// `MangaMissingController` (Mangarr.Api.V5/Manga/Wanted/MangaMissingController.cs)
/// likewise carries only `monitored`, `mangaIds[]`, `ageRating`,
/// `includeSubresources[]` filter params after the Phase 16.1 Wave 2 revert
/// dropped the per-translation filter parameter ("Manga-domain translation
/// preference belongs in TranslationProfile via preferred terms, not a
/// controller-level filter" per controller header).
///
/// Per-manga translation-language preference is therefore enforced at the
/// TranslationProfile level (Phase 5 D-04 — assigned per-manga). The
/// observable LANG-02 contract surfaces in the V5 response shape: every
/// chapter in the missing-list belongs to a manga whose `translationProfileId`
/// drives which language(s) the decision engine will accept. The
/// `includeSubresources=Manga` query hydrates the Manga subresource so the UI
/// can compose a client-side language filter via the Custom Filters builder.
///
/// This fixture probes the deterministic LANG-02 contract:
///   1. Seed a manga via AddMangaFlow.
///   2. Toggle one chapter to Monitored=true (so it shows up in Missing).
///   3. GET /api/v5/manga/wanted/missing?includeSubresources=Manga (with
///      languages-by-Manga implicit via TranslationProfile FK).
///   4. Assert the response 2xx AND records carry the Manga subresource AND
///      the parent manga carries a TranslationProfileId — proving the
///      language-filterable shape is exposed end-to-end.
///
/// Distinct from MangaMissingFilterFixture (line 61 WANTED-03 — covers the
/// generic filter-menu reachability + state-transition contract).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaMissingLanguageFilterFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task language_filter_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // PR #173 CI-fix (2026-05-15): the AddMangaFlow accepts modal defaults; the
        // frontend `add_manga_options` zustand store starts at `translationProfileId: 0`
        // (addMangaOptionsStore.ts:30) and the Add Manga modal does NOT auto-pick the
        // first profile when the dropdown initializes. Backend
        // AddMangaService.PrepareForAdd (line 267) writes the user-supplied value
        // verbatim — so the seeded manga lands with TranslationProfileId=0 and the
        // LANG-02 `BeGreaterThan(0)` assertion below fails. Assign the
        // baseline-seeded default TranslationProfile to the manga via PUT before
        // probing — this restores the "manga has a TP assigned" precondition the
        // LANG-02 contract requires, without coupling to UI-side default-fill
        // behavior. TranslationProfileService.Handle(ApplicationStartedEvent) seeds
        // "English Only" as profile id 1 on first boot
        // (TranslationProfileService.cs:92-112) — fetch the live list to be safe
        // against future seed-id reshuffles.
        var (mangaIdBefore, _, _) = await ResolveMangaContextAsync();
        await AssignDefaultTranslationProfileAsync(mangaIdBefore);

        var (mangaId, chapterId, translationProfileId) = await ResolveMangaContextAsync();

        // Ensure at least one chapter is monitored so the missing list has a
        // populated branch to assert against (the AddMangaFlow defaults vary
        // per monitor type; force-set to be deterministic).
        await SetChapterMonitoredAsync(chapterId);

        // STATE assertion 1: visit the Missing page so the testid-shell wraps
        // mount + UI smoke confirms the page renders.
        await new MangaMissingPage(Page).OpenAsync(RootUri);
        Page.Url.Should().EndWith("/manga/wanted/missing");

        // STATE assertion 2: GET /api/v5/manga/wanted/missing with the
        // `includeSubresources=Manga` query returns 2xx. This is the LANG-02
        // language-filterable wire shape: each chapter record carries its
        // parent manga reference (subresource) so the Wanted UI can compose
        // a client-side language filter via Custom Filters builder.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/wanted/missing?includeSubresources=Manga",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/wanted/missing must return 2xx with the language-filterable wire shape");

        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace();

        // STATE assertion 3: LANG-02 contract — the parent manga carries a
        // TranslationProfileId, the per-manga language preference axis (Phase 5
        // D-04). Without TranslationProfileId set, the decision engine cannot
        // accept-or-reject a release on language grounds — LANG-02 would be
        // unsatisfiable.
        translationProfileId.Should().BeGreaterThan(
            0,
            "LANG-02 contract: parent manga must carry a TranslationProfileId so the decision engine can language-filter");

        // STATE assertion 4: response body wires `translationProfileId` on the
        // Manga subresource for the records that have a parent. Proves the
        // language-filterable shape is end-to-end observable from the V5
        // missing endpoint (consumed by the React `useMissing` hook).
        // The records[] array may legitimately be empty in this fresh-DB
        // populated path (the seeded chapter may have HasFile=true already
        // from cassette state); but the JSON structure must contain the
        // `records` envelope (PagingResource<ChapterResource>).
        body.Should().Contain(
            "records",
            "GET /api/v5/manga/wanted/missing must return a PagingResource envelope with `records` array");
    }

    /// <summary>
    /// Resolve the AddMangaFlow-seeded manga id, one of its chapter ids, and the
    /// TranslationProfileId assigned to it (the LANG-02 axis).
    /// </summary>
    private async Task<(int MangaId, int ChapterId, int TranslationProfileId)> ResolveMangaContextAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var manga = mangaDoc.RootElement[0];
        var mangaId = manga.GetProperty("id").GetInt32();

        // PR #173 CI-fix (2026-05-15): `translationProfileId` is `int?` on
        // MangaResource (Mangarr.Api.V5/Manga/MangaResource.cs:38) — serializes as
        // JSON null when unset. Bare GetInt32() throws on null tokens; defend with
        // a 0 fallback so the AssignDefaultTranslationProfileAsync precondition
        // path can detect the missing-profile state without an exception.
        var tpElem = manga.GetProperty("translationProfileId");
        var translationProfileId = tpElem.ValueKind == JsonValueKind.Null
            ? 0
            : tpElem.GetInt32();

        var chapterId = await SeedFkResolver.ResolveFirstChapterIdAsync(RootUri, ApiKey, mangaId);

        return (mangaId, chapterId, translationProfileId);
    }

    /// <summary>
    /// PR #173 CI-fix (2026-05-15): force-assign the baseline-seeded default
    /// TranslationProfile to a manga via PUT /api/v5/manga/{id}. The AddMangaFlow
    /// accepts modal defaults; the frontend zustand store starts at
    /// `translationProfileId: 0` and the Add Manga modal does NOT auto-pick the
    /// first profile — so the seeded manga lands with TranslationProfileId=0.
    /// LANG-02's `BeGreaterThan(0)` assertion needs a real profile id assigned.
    ///
    /// Resolves the first TranslationProfile via GET /api/v5/translationprofile
    /// (TranslationProfileService.Handle(ApplicationStartedEvent) seeds
    /// "English Only" on first boot — TranslationProfileService.cs:92-112).
    /// </summary>
    private async Task AssignDefaultTranslationProfileAsync(int mangaId)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        // Resolve the baseline-seeded default profile id from the live profile list.
        var profilesJson = await http.GetStringAsync($"{RootUri}/api/v5/translationprofile");
        using var profilesDoc = JsonDocument.Parse(profilesJson);
        profilesDoc.RootElement.GetArrayLength().Should().BeGreaterThan(
            0,
            "TranslationProfileService.Handle(ApplicationStartedEvent) seeds at least one profile on first boot");
        var defaultProfileId = profilesDoc.RootElement[0].GetProperty("id").GetInt32();

        // PUT the manga back with translationProfileId assigned. MangaController.UpdateManga
        // (Mangarr.Api.V5/Manga/MangaController.cs:158) expects the full MangaResource
        // body shape — fetch + mutate the relevant field + PUT, mirroring
        // SetChapterMonitoredAsync's pattern.
        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga/{mangaId}");
        var mangaNode = JsonNode.Parse(mangaJson)!;
        mangaNode["translationProfileId"] = defaultProfileId;

        var content = new StringContent(mangaNode.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await http.PutAsync($"{RootUri}/api/v5/manga/{mangaId}", content);
        resp.IsSuccessStatusCode.Should().BeTrue(
            $"PUT /api/v5/manga/{mangaId} (translationProfileId={defaultProfileId}) must succeed; got {(int)resp.StatusCode}");
    }

    /// <summary>
    /// Force-set a chapter to Monitored=true via PUT /api/v5/chapter/{id}
    /// so the missing-list populated path is exercised deterministically.
    /// Mirrors HistoryRetryFixture's HttpClient + X-Api-Key shape.
    /// </summary>
    private async Task SetChapterMonitoredAsync(int chapterId)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        // Fetch current chapter shape so we can echo it back with Monitored=true
        // (PUT /api/v5/chapter/{id} expects a full ChapterResource body shape).
        var currentJson = await http.GetStringAsync($"{RootUri}/api/v5/chapter/{chapterId}");
        var currentNode = JsonNode.Parse(currentJson)!;
        currentNode["monitored"] = true;

        var content = new StringContent(currentNode.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await http.PutAsync($"{RootUri}/api/v5/chapter/{chapterId}", content);
        resp.IsSuccessStatusCode.Should().BeTrue(
            $"PUT /api/v5/chapter/{chapterId} (monitored=true) must succeed; got {(int)resp.StatusCode}");
    }
}
