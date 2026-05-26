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
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `OrganizePreviewModal` (Manga bulk Organize preview).
///
/// W#6 state body assertion (revision iteration 1): consume the Plan 20-01
/// SeedChapterFileAsync precondition AND assert the response carries >= 1
/// organize/rename entry. State assertion — NOT status-code-only.
///
/// The OrganizePreviewModal React subtree is an upstream stub at
/// frontend/src/Organize/OrganizePreviewModal.tsx (Phase 17.3 carry-over,
/// never wired to a Mangarr consumer — the equivalent feature lives at the
/// per-manga GET /api/v5/manga/rename single-id endpoint). The preview API
/// surface IS the modal's data source; exercising the v5 contract directly
/// is the canonical Blocker #4-path-c shape (mirrors Plan 20-07b
/// ArchiveFormatDefaultFixture which directly asserts on the canonical API
/// surface when the React entry-point is not wired in Mangarr v1).
///
/// Flow: AddMangaFlow seed → resolve manga + chapter FKs → SeedChapterFileAsync
/// (Plan 20-01) → Page.APIRequest GET /api/v5/manga/rename?mangaId={id} →
/// assert >= 1 organize/rename entry in response.
///
/// Blocker #4 path (b): SeedChapterFileAsync seeder is the deterministic
/// precondition; zero inconclusive-skip branches.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class OrganizePreviewModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task preview_renders()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();

        var testKit = new TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedChapterFileAsync(
            Runner.AppData,
            mangaId,
            chapterId,
            "TestKit Manga - Chapter 1 [en].cbz");

        // Hit GET /api/v5/manga/rename?mangaId={id} — single-manga rename preview
        // (the canonical Organize preview data source; the UI Modal subtree exists
        // at frontend/src/Organize/OrganizePreviewModal.tsx but is not wired in V1).
        var response = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/rename?mangaId={mangaId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        response.Status.Should().Be(200,
            "GET /api/v5/manga/rename must return 200 with a seeded ChapterFile precondition");

        var body = await response.TextAsync();
        body.Should().Contain(
            "existingPath",
            "rename preview response must contain existingPath markers (W#6 state assertion)");
        body.Should().Contain(
            "newPath",
            "rename preview response must contain newPath markers (W#6 state assertion)");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1,
            "Organize preview must contain >= 1 organize entry per W#6 state assertion (not status-code-only)");
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
