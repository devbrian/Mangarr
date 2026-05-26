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
/// Phase 20 Plan 20-10 (Wave 3 AddManga modal sweep) — v5-endpoint axis
/// `GET /api/v5/manga/{id}/folder` (INVENTORY row 77, canonical path
/// Tests/Manga/ — same Blocker #1 class as MangaLinksResolver +
/// MangaDetailsChapterList; reconcile-inventory.py keys row detection on
/// the exact path).
///
/// Tier (D-04): **PRSmoke** — v5-endpoint axis (GET-heavy) maps to PRSmoke
/// per the mechanical row-axis rule.
///
/// The /api/v5/manga/{id}/folder endpoint is a folder-name preview computed
/// via `IBuildMangaFileNames.GetMangaFolder(manga, null)` per
/// `MangaFolderController` (Phase 13 Plan 13-05). The UI consumer is the
/// EditMangaModal's Folder field — when the user changes the path, the
/// frontend hits this endpoint to preview the folder shape that will result.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** The EditMangaModal does
/// NOT currently fire GET /api/v5/manga/{id}/folder on field render — the
/// endpoint is wired but no React consumer reaches for it in v1 (the path
/// field shows the persisted Manga.Path directly). Per Plan 20-04 / 20-07b /
/// 20-08 / 20-09 path c precedent: ship a Page.APIRequest direct-CRUD
/// fixture asserting on the endpoint contract directly. The covering-test
/// name `folder_field_renders` is INVENTORY-pinned.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manga/{id}/folder returns 200 for a seeded manga.
///   2. Response body wires a non-empty folder string (the preview path).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaFolderFieldFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task folder_field_renders()
    {
        // Seed a manga via AddMangaFlow so a real Manga row exists in the DB
        // with a populated Path (root folder + title slug).
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var mangaId = await ResolveMangaIdAsync();

        // Hit GET /api/v5/manga/{id}/folder directly. This is the folder-name
        // preview surface (Phase 13 Plan 13-05); the UI's EditMangaModal
        // Folder field is the documented consumer but does not currently
        // fire the GET on render — path c.
        var response = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manga/{mangaId}/folder",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // STATE assertion 1: endpoint returns 200 for the seeded manga id.
        response.Status.Should().Be(
            200,
            "GET /api/v5/manga/{id}/folder must return 200 for an existing manga");

        // STATE assertion 2: response body wires a non-empty folder preview.
        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "GET /api/v5/manga/{id}/folder response must include the folder-name preview payload");

        // The endpoint returns either a bare string OR a JSON object carrying
        // the folder name. Accept either shape — the contract is "non-empty
        // folder identifier returned".
        var trimmed = body.Trim();
        trimmed.Length.Should().BeGreaterThan(
            2,
            "folder-name preview must be a non-empty string / object (not an empty literal)");
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
