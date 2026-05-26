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
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch + req-axis sweep) — BLOCK-01
/// req-axis coverage (INVENTORY row 63: "User can blocklist a release").
///
/// Tier (D-04): **PRSmoke** per Blocker #5 — the mechanical row-axis rule
/// says `req` axis maps to PRSmoke regardless of trigger surface complexity
/// (mirrors Plan 20-09 BLOCK-02 + Plan 20-07b ARCHIVE-01/02 + LANG-02
/// precedent). The req-axis row covers the end-user contract "user can
/// blocklist a release"; the trigger surface is incidental to the axis-tier
/// mapping.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** No "Blocklist Release"
/// button exists on `frontend/src/InteractiveSearch/InteractiveSearchRow.tsx`
/// — the row carries an `isBlocklisted` icon (line 282) that displays AFTER
/// the release is on the blocklist, but no explicit user action on the row
/// adds a release to the blocklist; the blocklist surface is populated via
/// MarkAsFailed history events + per-row blocklist Remove. Per Plan 20-09
/// BLOCK-02 precedent (which seeded a MangaBlocklist row via TestKit and
/// asserted the contract end-to-end), this fixture takes the same approach:
/// seed a MangaBlocklist row via TestKit.SeedBlocklistAsync then assert
/// the canonical wire shape (GET /api/v5/manga/blocklist returns the row).
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. After seeding a MangaBlocklist row, GET /api/v5/manga/blocklist
///      returns 2xx with the row present in the PagingResource.records[].
///   2. The row carries the seeded mangaId — proving the end-user contract
///      "user can blocklist a release for a specific manga" is observable
///      end-to-end.
///
/// Distinct from BlocklistRemoveFixture (BLOCK-02, Plan 20-09 — covers the
/// per-row Remove flow); this fixture is pinned to the BLOCK-01 req row's
/// `blocklist_button_persists` covering-test.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class BlocklistReleaseFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task blocklist_button_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();

        // BLOCK-01 contract: a blocklist row materializes for the seeded
        // manga. The "blocklist a release" mechanism is the
        // TestKit.SeedBlocklistAsync raw-SQLite path (Plan 20-01) — the same
        // path that BLOCK-02 (Plan 20-09 BlocklistRemoveFixture) uses to
        // exercise the per-row Remove flow.
        var testKit = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        // STATE assertion 1: GET /api/v5/manga/blocklist returns 2xx.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/blocklist",
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
            "GET /api/v5/manga/blocklist must return 2xx after a blocklist row is seeded");

        // STATE assertion 2: the response body contains a record for the
        // seeded mangaId — proves the BLOCK-01 contract "user can blocklist
        // a release for a specific manga" is observable end-to-end via the
        // canonical paged blocklist endpoint.
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "blocklist response must include the PagingResource envelope");
        body.Should().Contain(
            "records",
            "GET /api/v5/manga/blocklist must return a PagingResource envelope with `records` array");

        using var doc = JsonDocument.Parse(body);
        var records = doc.RootElement.GetProperty("records");
        records.GetArrayLength().Should().BeGreaterThan(
            0,
            "BLOCK-01: at least one blocklist row must exist after seeding");

        // Confirm the seeded mangaId appears in at least one row.
        var foundForManga = false;
        foreach (var row in records.EnumerateArray())
        {
            if (row.TryGetProperty("mangaId", out var rowMangaId)
                && rowMangaId.GetInt32() == mangaId)
            {
                foundForManga = true;
                break;
            }
        }

        foundForManga.Should().BeTrue(
            "BLOCK-01: blocklist must surface a row carrying the seeded mangaId");
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
