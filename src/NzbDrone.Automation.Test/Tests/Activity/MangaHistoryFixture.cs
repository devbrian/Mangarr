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

// Phase 18 Plan-05: History cluster coverage.
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08) then seeds a real
// `DownloadFailed` ChapterHistory row via `TestKit.SeedHistoryFailedAsync`
// (Plan 19-01 raw-SQLite verdict, mirroring the HistoryRetryFixture / Plan
// 19-05 precedent and gh-#115's MangaBlocklistFixture rework). Asserts on the
// decision cell per feedback_verify_ui_state_not_just_rendering.md — the
// `manga-history-row-{id}-decision` cell is the one that hides silent rejection
// icons (Phase 2 retro precedent). Every rendered row must expose its decision
// cell with a non-empty `data-event-type` attribute, otherwise the silent-row
// regression has resurfaced.
//
// gh #152 (Class 1 — Playwright locator visibility) fix-forward: the previous
// version relied on cassette state for history contents and treated an empty
// History as "valid coverage" — but History.tsx (line 170-208) only mounts the
// `manga-history-page` / `manga-history-table` testid wrappers inside the
// `records.length > 0` branch. AddMangaFlow does not produce any ChapterHistory
// rows (a clean grab event never fires through the cassette), so the shell
// ToBeVisibleAsync assertions on the testids were unsatisfiable on the fresh
// per-fixture DB baseline. Switched to the seed-then-assert pattern so the
// populated branch is exercised deterministically — same approach as
// HistoryRetryFixture and MangaBlocklistFixture.
[TestFixture]
[Category("AutomationTest")]
public class MangaHistoryFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task history_page_renders_table_with_decision_cells_when_rows_present()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // gh #152: seed a real DownloadFailed ChapterHistory row via the
        // raw-SQLite TestKit helper (Plan 19-01 verdict). Resolve the
        // AddMangaFlow-seeded manga + one of its chapters as the FKs, then
        // INSERT into the backend's per-fixture mangarr.db BEFORE navigating.
        // Without this seed, the cassette-only state leaves History empty and
        // the testid-wrapped table never mounts — the assertions below cannot
        // be satisfied.
        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedHistoryFailedAsync(Runner.AppData, mangaId, chapterId);

        await new MangaHistoryPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present (page + table testids). With the
        // seed in place above, the populated branch in History.tsx renders
        // these wrappers; if the page-load itself failed or the seed silently
        // produced zero rows, both assertions fail fast.
        await Assertions.Expect(Page.GetByTestId("manga-history-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-history-table")).ToBeVisibleAsync();

        // STATE assertion 2 (silent-rejection-icon-hiding-cell):
        // every visible history row MUST expose its decision cell. Without this
        // check, a row that rendered without its event-type icon would pass a
        // naive "rows.Count > 0" test — exactly the class of regression the
        // feedback memo guards against.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        var count = await rowsLocator.CountAsync();
        count.Should().BeGreaterThan(0, "seed must have produced a failed history row");

        for (var i = 0; i < count; i++)
        {
            var row = rowsLocator.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-history-row-", string.Empty);

            var decisionCell = Page.GetByTestId($"manga-history-row-{rowId}-decision");
            await Assertions.Expect(decisionCell).ToBeVisibleAsync();

            // STATE assertion 3 (decision state, not just rendering):
            // the cell exposes its event-type so the silent-empty-icon case is
            // caught. data-event-type is wired in HistoryEventTypeCell.tsx.
            var eventType = await decisionCell.GetAttributeAsync("data-event-type");
            eventType.Should().NotBeNullOrEmpty();
        }

        Page.Url.Should().EndWith("/manga/activity/history");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — these are the FKs the raw-SQLite seed helper needs. The
    // chapter set is populated from the MangaDex /feed cassette during the
    // AddManga flow, so at least one chapter exists by the time this runs.
    // Mirrors HistoryRetryFixture.ResolveSeedFksAsync (Plan 19-05).
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
