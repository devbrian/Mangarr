using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — Blocklist Details modal
/// coverage (INVENTORY modal-action row 164: BlocklistDetailsModal).
///
/// Tier (D-04): Nightly per the axis-based heuristic — `modal-action` row.
///
/// Seeds a MangaBlocklist row via TestKit.SeedBlocklistAsync (Plan 19-01 D-04
/// raw-SQLite verdict). Clicks the per-row Details icon button
/// (BlocklistRow.tsx:201 — aria-label "Details") → BlocklistDetailsModal
/// opens. Asserts the modal renders + content includes the seeded source
/// title (proves the seeded row's data round-tripped through the V5
/// projection AND the modal rendered its content branch).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class BlocklistDetailsModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;
    private const string SeededSourceTitle = "TestKit Seeded Release - Chapter";

    [Test]
    public async Task detail_renders()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated blocklist table mounts.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "seed must have produced a blocklist row");

        // BlocklistRow.tsx:201 exposes the per-row icon-only IconButton with
        // aria-label="Details" → opens BlocklistDetailsModal.
        var firstRow = rowsLocator.First;
        var detailsButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Details" });
        await detailsButton.ClickAsync();

        // STATE assertion 2: BlocklistDetailsModal renders (role=dialog).
        var dialog = Page.GetByRole(AriaRole.Dialog).First;
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion 3: the modal renders the seeded SourceTitle —
        // proves the V5 projection round-trip AND the modal's content branch
        // ran (BlocklistDetailsModal renders the row's release-identity fields).
        var dialogText = await dialog.TextContentAsync();
        dialogText.Should().Contain(
            SeededSourceTitle,
            "BlocklistDetailsModal must render the seeded SourceTitle");
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
