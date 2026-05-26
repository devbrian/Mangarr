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
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `GET /api/v5/manga/rename/bulk` (Bulk-rename preview).
///
/// W#6 state body assertion (revision iteration 1): consume the Plan 20-01
/// SeedChapterFileAsync precondition AND assert the response body contains
/// >= 1 rename entry (existingPath + newPath markers from RenameChapterResource).
/// State assertion — NOT status-code-only.
///
/// Mangarr does not (yet) wire a frontend Mass-Editor route that triggers GET
/// /api/v5/manga/rename/bulk, so this fixture exercises the v5-endpoint
/// contract directly via Page.APIRequest after seeding the precondition state.
/// This mirrors Plan 20-07a's RootFolderAddFixture / RemotePathMappingCrudFixture
/// shape (direct Page.APIRequest CRUD when no UI form selector exists at that
/// row depth). Per Blocker #4 path (a/b): the SeedChapterFileAsync seeder is
/// the deterministic precondition; zero inconclusive-skip branches.
///
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BulkRenamePreviewFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_preview_renders()
    {
        // Seed manga via AddMangaFlow so a real Manga + Chapter row pair exists in DB.
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Resolve the seeded manga id + one chapter id (FKs for SeedChapterFileAsync).
        var (mangaId, chapterId) = await ResolveSeedFksAsync();

        // W#6 precondition: seed a ChapterFile row pointing at a deterministic
        // disk path so the rename preview has SOMETHING to rename. Per Plan 20-01
        // SeedChapterFileAsync (raw-SQLite per Phase 19 D-04 lineage).
        var testKit = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedChapterFileAsync(
            Runner.AppData,
            mangaId,
            chapterId,
            "TestKit Manga - Chapter 1 [en].cbz");

        // Hit GET /api/v5/manga/rename/bulk?mangaIds={id} directly. Mangarr does
        // not (yet) wire a frontend Mass-Editor route that triggers this endpoint,
        // so the v5-endpoint contract is exercised via Page.APIRequest. Mirrors
        // Plan 20-07a RootFolderAddFixture's direct-CRUD shape.
        var response = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/rename/bulk?mangaIds={mangaId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // STATE assertion (status): GET /api/v5/manga/rename/bulk returns 200.
        response.Status.Should().Be(200,
            "GET /api/v5/manga/rename/bulk must return 200 with a seeded ChapterFile precondition");

        // W#6 STATE assertion (body): response contains rename entry markers
        // existingPath + newPath (RenameChapterResource fields). Not status-only.
        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "rename/bulk response must include at least one rename entry per W#6");
        body.Should().Contain(
            "existingPath",
            "rename/bulk response body must carry existingPath markers (W#6 state assertion, not status-code-only)");
        body.Should().Contain(
            "newPath",
            "rename/bulk response body must carry newPath markers (W#6 state assertion, not status-code-only)");

        // STATE assertion (count): response JSON is a non-empty list with >= 1 entry.
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array,
            "rename/bulk response must be a JSON array of RenameChapterResource");
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "rename/bulk response must contain >= 1 rename entry per W#6 state assertion");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — Plan 20-01 SeedChapterFileAsync requires these as FKs.
    // Mirrors Plan 19-05 BlocklistBulkRemoveFixture's ResolveSeedFksAsync.
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
