using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B / D-12 round-trip) — L-002
// first-record-creation smoke for the event-driven auto-exclusion path.
//
// Phase 27 retarget (closes GH #217): originally registered TestImportList and
// relied on ImportListSync to produce a Manga record, which fails because
// TestImportList is excluded from production DI (Pitfall 2). Now:
//   1. Registers MangaDexImportList (real Phase 27 provider) so the row exists.
//   2. Seeds a Manga directly via AddMangaFlow.AddByMangaDexIdAsync (cassette-
//      replayed MangaDex lookup — works without working OAuth).
//   3. Deletes the Manga via DELETE /api/v5/manga/{id}; MangaService.DeleteManga
//      defaults addImportListExclusion: true per Phase 26 Plan 26-04 D-12.
//   4. Polls GET /api/v5/importlistexclusion (≤5s; Pitfall 3 async-flush
//      tolerance for IHandle<MangaDeletedEvent>).
//   5. Asserts an ImportListExclusion row exists with the matching
//      MangaDexId / MalId / AniListId triplet.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors.
[TestFixture]
[Category("AutomationTest")]
public class AutoExclusionOnDeleteFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAndRegisterProviderAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await tk.DisableComixIndexerAsync();
#pragma warning restore CS0618

        // Register MangaDexImportList — the actual exclusion-on-delete path
        // does NOT need the sync to fire (we seed the Manga via AddMangaFlow
        // below). The registration is here for parity with the Phase 26 bucket-B
        // narrative ("ImportList registered → Manga deleted → exclusion fires").
        var (_, _) = await tk.RegisterMangaDexImportListAsync("MangaDex (auto-exclusion)");
    }

    [Test]
    public async Task delete_manga_auto_adds_exclusion_via_event_handler()
    {
        // 1. Seed a Manga via the UI flow (cassette-replayed MangaDex lookup +
        //    AddMangaModal Confirm). This guarantees a real Manga row with a
        //    valid MangaDexId in the DB — the dummy-cred MangaDex sync would
        //    produce 0 records.
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // 2. Pull the seeded Manga's id + triplet. AddMangaFlow uses the canonical
        //    KnownMangaDexId; locate it by GUID match.
        var mangaListResp = await http.GetAsync("manga");
        mangaListResp.IsSuccessStatusCode.Should().BeTrue("GET /api/v5/manga must return 2xx");
        using var mangaListDoc = JsonDocument.Parse(await mangaListResp.Content.ReadAsStringAsync());
        mangaListDoc.RootElement.GetArrayLength().Should().BeGreaterThan(
            0,
            "AddMangaFlow.AddByMangaDexIdAsync must have persisted at least one Manga record");

        var mangaId = 0;
        string mangaDexId = null;
        int? malId = null;
        int? aniListId = null;

        foreach (var element in mangaListDoc.RootElement.EnumerateArray())
        {
            var elementMdxProp = element.TryGetProperty("mangaDexId", out var mdxProp) && mdxProp.ValueKind == JsonValueKind.String
                ? mdxProp.GetString()
                : null;

            if (string.Equals(elementMdxProp, KnownMangaDexId, StringComparison.OrdinalIgnoreCase))
            {
                mangaId = element.GetProperty("id").GetInt32();
                mangaDexId = elementMdxProp;
                malId = element.TryGetProperty("malId", out var malProp) && malProp.ValueKind == JsonValueKind.Number
                    ? malProp.GetInt32()
                    : (int?)null;
                aniListId = element.TryGetProperty("aniListId", out var aniProp) && aniProp.ValueKind == JsonValueKind.Number
                    ? aniProp.GetInt32()
                    : (int?)null;
                break;
            }
        }

        mangaId.Should().BeGreaterThan(
            0,
            "AddMangaFlow seed must produce a Manga with mangaDexId={0}",
            KnownMangaDexId);

        // 3. DELETE the Manga. addImportListExclusion defaults true per
        //    MangaService.cs:155-162; no query param required.
        var deleteResp = await http.DeleteAsync($"manga/{mangaId}");
        deleteResp.IsSuccessStatusCode.Should().BeTrue(
            "DELETE /api/v5/manga/{0} must return 2xx (body: {1})",
            mangaId,
            await deleteResp.Content.ReadAsStringAsync());

        // 4. Pitfall 3 async-flush race tolerance — poll for up to 5s. The
        //    IHandle<MangaDeletedEvent> handler runs through EventAggregator
        //    which can flush asynchronously; a tight assertion races the
        //    event-bus dispatch.
        var exclusionDeadline = DateTime.UtcNow.AddSeconds(5);
        var exclusionFound = false;
        var lastBody = string.Empty;
        while (DateTime.UtcNow < exclusionDeadline)
        {
            var exclusionsResp = await http.GetAsync("importlistexclusion");
            if (exclusionsResp.IsSuccessStatusCode)
            {
                lastBody = await exclusionsResp.Content.ReadAsStringAsync();
                using var exclusionsDoc = JsonDocument.Parse(lastBody);
                foreach (var rec in exclusionsDoc.RootElement.GetProperty("records").EnumerateArray())
                {
                    var mangaDexMatch = mangaDexId != null
                        && rec.TryGetProperty("mangaDexId", out var rMdx)
                        && rMdx.ValueKind == JsonValueKind.String
                        && rMdx.GetString() == mangaDexId;

                    var malMatch = malId.HasValue
                        && rec.TryGetProperty("malId", out var rMal)
                        && rMal.ValueKind == JsonValueKind.Number
                        && rMal.GetInt32() == malId.Value;

                    var aniMatch = aniListId.HasValue
                        && rec.TryGetProperty("aniListId", out var rAni)
                        && rAni.ValueKind == JsonValueKind.Number
                        && rAni.GetInt32() == aniListId.Value;

                    // Full-record match: for each input identifier that was actually
                    // populated on the seeded Manga, require the exclusion row's
                    // corresponding field to match. OR-matching would silently accept
                    // a row with only one matching field (e.g., a coincidental MalId
                    // collision on an unrelated exclusion record).
                    var fullMatch =
                        (mangaDexId == null || mangaDexMatch) &&
                        (!malId.HasValue || malMatch) &&
                        (!aniListId.HasValue || aniMatch) &&
                        (mangaDexMatch || malMatch || aniMatch); // at least one must actually have hit

                    if (fullMatch)
                    {
                        exclusionFound = true;
                        break;
                    }
                }
            }

            if (exclusionFound)
            {
                break;
            }

            await Task.Delay(250);
        }

        // 5. State assertion: an ImportListExclusion row with the matching
        //    triplet exists.
        exclusionFound.Should().BeTrue(
            "deleted Manga (id={0}, mangaDexId={1}, malId={2}, aniListId={3}) should " +
            "auto-add an ImportListExclusion row within 5s via " +
            "ImportListExclusionService.Handle(MangaDeletedEvent). " +
            "Last GET /api/v5/importlistexclusion body: {4}",
            mangaId,
            mangaDexId,
            malId,
            aniListId,
            lastBody);
    }
}
