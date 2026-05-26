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
/// Phase 20 Plan 20-09 (Wave 3 Activity modal sweep) — BLOCK-02 req-axis
/// coverage (INVENTORY req row 64: "User can remove blocklist entries").
///
/// Tier (D-04): **PRSmoke** per Blocker #5 — the mechanical row-axis rule
/// says `req` axis maps to PRSmoke, regardless of trigger surface
/// complexity (mirrors ARCHIVE-01/02 in Plan 20-07b and LANG-02 in this
/// same plan). The req-axis row covers the end-user contract "user can
/// remove a blocklist entry"; the modal-action interaction surface is
/// incidental to the axis-tier mapping.
///
/// Seeds a manga via AddMangaFlow + a MangaBlocklist row via
/// TestKit.SeedBlocklistAsync (Plan 19-01 raw-SQLite verdict). Clicks the
/// per-row Remove icon button (BlocklistRow.tsx:208 — aria-label
/// "Remove from Blocklist") and asserts the row detaches AND the total
/// row count decremented (real STATE transition, not just modal close).
///
/// Distinct from BlocklistBulkRemoveFixture (DELETE /api/v5/manga/blocklist/bulk,
/// v5-endpoint axis row 89) and MangaBlocklistFixture (the Phase 18 Plan-05
/// blocklist-cluster fixture covering the same per-row DELETE flow — this is
/// the req-axis-specific fixture pinned to the BLOCK-02 INVENTORY row).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class BlocklistRemoveFixture : AutomationTest
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
    public async Task remove_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: populated table shell mounts.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();
        countBefore.Should().BeGreaterThan(0, "seed must have produced a blocklist row");

        var firstRow = rowsLocator.First;
        var idAttr = await firstRow.GetAttributeAsync("data-testid");
        var rowId = idAttr!.Replace("manga-blocklist-row-", string.Empty);

        // BlocklistRow.tsx mounts a per-row remove button as the dedicated
        // testid `manga-blocklist-row-{id}-remove-button` (the same surface
        // MangaBlocklistFixture greens). Click it — BlocklistRow handles the
        // DELETE inline (no confirmation modal — the row detaches directly).
        var doomedRow = Page.GetByTestId($"manga-blocklist-row-{rowId}");
        await Page.GetByTestId($"manga-blocklist-row-{rowId}-remove-button").ClickAsync();

        // STATE assertion 2: the specific row detaches (not just "some row
        // was removed somewhere").
        await doomedRow.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10_000
        });

        // STATE assertion 3: the total count decremented — defends against
        // the failure mode where the doomed-row testid is intact but
        // detached (React re-rendered with stale state).
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$")).CountAsync();
        countAfter.Should().Be(
            countBefore - 1,
            "BLOCK-02 contract: per-row blocklist Remove must decrement total count");

        Page.Url.Should().EndWith("/manga/activity/blocklist");
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
