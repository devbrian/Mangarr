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

// Phase 18 Plan-05: Queue cluster coverage.
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08 — UI-populates-via-UI)
// then seeds a real MangaPendingReleases row via TestKit.SeedPendingQueueItemAsync
// (Plan 19-01 raw-SQLite verdict, mirroring the QueueRowRemoveFixture / Plan
// 19-05 precedent). Navigates to the Queue page and asserts on STATE
// (decision/status cell presence on every visible row) per
// feedback_verify_ui_state_not_just_rendering.md.
//
// gh #152 (Class 1 — Playwright locator visibility) fix-forward: the previous
// version relied on cassette state for queue contents and treated an empty
// Queue as "valid coverage" — but Queue.tsx (line 256-300) only mounts the
// `manga-queue-page` / `manga-queue-table` testid wrappers inside the
// `records.length > 0` branch. AddMangaFlow does not produce queue rows (the
// in-memory queue is structurally dead per D-03; MangaPendingReleases is the
// only live source), so the shell ToBeVisibleAsync assertions were
// unsatisfiable on the fresh per-fixture DB baseline. Switched to the
// seed-then-assert pattern so the populated branch is exercised
// deterministically — same approach as QueueRowRemoveFixture.
[TestFixture]
[Category("AutomationTest")]
public class MangaQueueFixture : AutomationTest
{
    // MangaBaka id for "Solo Leveling" — stable cassette anchor per
    // AddMangaFlow.KnownMangaBakaId. The exact chapter set is deterministic via
    // cassette; the fixture asserts on row SHAPE (status cell presence), not
    // row count.
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task queue_page_renders_table_with_state_assertions_on_visible_rows()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // gh #152: seed a real MangaPendingReleases row via the raw-SQLite
        // TestKit helper (Plan 19-01 verdict — D-03 queue seam). Capture the
        // AddMangaFlow-seeded manga id + title, then INSERT into the backend's
        // per-fixture mangarr.db BEFORE navigating. SeedPendingQueueItemAsync
        // also triggers the static _pendingReleases cache rebuild so the row
        // surfaces in GET /api/v5/manga/queue. Without this seed, Queue.tsx
        // renders the empty-state Alert (peer-level, no shell testid) instead
        // of the testid-wrapped table.
        var (mangaId, mangaTitle) = await ResolveSeededMangaAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedPendingQueueItemAsync(Runner.AppData, mangaId, mangaTitle);

        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table containers present (sane shell). With
        // the seed in place above, the populated branch in Queue.tsx renders
        // these wrappers.
        await Assertions.Expect(Page.GetByTestId("manga-queue-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();

        // STATE assertion 2 (per feedback_verify_ui_state_not_just_rendering.md):
        // if ANY rows render, EACH row exposes its status cell — silent-empty-row
        // rendering would fail this check. The selector targets row containers
        // (testids of the form `manga-queue-row-{id}` with no trailing -{cell}).
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var count = await rowsLocator.CountAsync();
        count.Should().BeGreaterThan(
            0,
            "D-03 seed must have produced a MangaPendingReleases queue row");

        for (var i = 0; i < count; i++)
        {
            var row = rowsLocator.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-queue-row-", string.Empty);

            var statusCell = Page.GetByTestId($"manga-queue-row-{rowId}-status");
            await Assertions.Expect(statusCell).ToBeVisibleAsync();
        }

        // URL stability (final shell-level assertion — confirms no spurious nav).
        Page.Url.Should().EndWith("/manga/activity/queue");
    }

    // Resolve the AddMangaFlow-seeded manga id + title via the V5 API — the
    // id is the FK and the title builds the canonical-shaped ParsedChapterInfo
    // the raw-SQLite SeedPendingQueueItemAsync helper persists. Mirrors
    // QueueRowRemoveFixture.ResolveSeededMangaAsync (Plan 19-05).
    private async Task<(int MangaId, string MangaTitle)> ResolveSeededMangaAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var mangaJson = await http.GetStringAsync($"{RootUri}/api/v5/manga");
        using var mangaDoc = JsonDocument.Parse(mangaJson);
        var manga = mangaDoc.RootElement[0];
        var mangaId = manga.GetProperty("id").GetInt32();
        var mangaTitle = manga.GetProperty("title").GetString()!;

        return (mangaId, mangaTitle);
    }
}
