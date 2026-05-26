using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

// Phase 18 Plan-06: Wanted/CutoffUnmet cluster coverage.
//
// gh #153 fix-forward (extends gh #152 Class 1 patch): the previous version asserted
// only the always-mounted page-shell testid because the fresh-DB baseline could not
// trigger the populated row-state branch — TranslationProfile "English Only" is
// single-language (NOT below cutoff per ChapterCutoffService:68-76) and the default
// CustomFormatProfile has MinFormatScore=0 + MaxFormatScore=null (NOT below cutoff
// per ChapterCutoffService:81-90), so /api/v5/manga/wanted/cutoff returned an empty
// page even when a manga was seeded. The row-state assertion block was guarded by
// `if (tableCount > 0)` and silently skipped — a test-coverage gap that "passed"
// without exercising the populated-path contract.
//
// gh #153 closes the gap with three coordinated changes:
//   1. TestKit.SeedCutoffUnmetChapterAsync — creates a multi-language
//      TranslationProfile (Languages.Count > 1 ⇒ in below-cutoff list per
//      ChapterCutoffService:68-76), re-assigns the manga to it, seeds a ChapterFile
//      row via raw-SQLite (mirroring SeedHistoryFailedAsync precedent), updates the
//      Chapter's ChapterFileId + forces Monitored=true.
//   2. ChapterRepository.ChaptersWhereCutoffUnmet — drops the `.Value` member access
//      on `m.TranslationProfileId.Value` / `m.CustomFormatProfileId.Value` (the
//      WhereBuilder cannot translate `.Value` on a Nullable<int> and emits SQL
//      `IN (NULL)`, so the candidateMangaIds projection always returned empty);
//      also forces the bool comparator to `c.Monitored == true` (a bare bool
//      member-access expression trips WhereBuilder's "requires a concrete
//      condition" guard). Both are latent bugs that this fixture catches.
//   3. This fixture — drops the empty-state branch entirely. The populated path is
//      the only path (Plan 19-05 D-03 precedent: populated-only assertions, no
//      conditional skip). Mirrors HistoryRetryFixture / BlocklistBulkRemoveFixture /
//      QueueRowRemoveFixture shape.
//
// /gsd-debug nightly-26025833226-postgres-automation fix: the populated-table
// testid visibility assertion (line 74) was hitting Playwright's default 5s
// timeout under postgres 17/18 cold-start timing — the GET
// /api/v5/manga/wanted/cutoff query path (multi-lang TranslationProfile +
// ChapterFile join + ChaptersWhereCutoffUnmet WhereBuilder predicate) is slow
// to first-paint on a fresh postgres DB. Bumped the assertion's timeout to 30s
// (matching the QueueTableLoadFixture / HistoryTableLoadFixture precedent for
// first-paint cold-start waits). Postgres 16 + sqlite both passed at the
// default — only 17/18 needed the headroom.
[TestFixture]
[Category("AutomationTest")]
public class MangaCutoffUnmetFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task cutoff_unmet_page_renders_table_with_state_cells()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // gh #153: seed below-cutoff state via the TestKit helper. Capture the
        // AddMangaFlow-seeded manga + one of its chapters as the FKs, then create
        // a multi-language TranslationProfile, re-assign the manga, INSERT a
        // ChapterFile row + UPDATE the Chapter BEFORE navigating to the
        // cutoff-unmet page.
        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedCutoffUnmetChapterAsync(Runner.AppData, mangaId, chapterId);

        await new MangaCutoffUnmetPage(Page).OpenAsync(RootUri);

        // STATE assertion 1 (page shell): the page-level testid wraps both the
        // empty-state Alert and the populated table branch (CutoffUnmet.tsx line
        // 296), always-mounted.
        await Assertions.Expect(Page.GetByTestId("manga-cutoff-unmet-page")).ToBeVisibleAsync();

        // STATE assertion 2 (populated table testid): the seed must produce a
        // table mount. CutoffUnmet.tsx line 314 mounts `manga-cutoff-unmet-table`
        // only inside the `records.length > 0` branch — a silent seed failure
        // (e.g. profile not re-assigned, ChapterFile not inserted) fails the
        // test loudly here rather than skipping past an empty page.
        //
        // /gsd-debug nightly-26025833226-postgres-automation: 30s timeout (up
        // from default 5s) — postgres 17/18 cold-start first-paint of the
        // cutoff-unmet query exceeds 5s. Matches QueueTableLoadFixture /
        // HistoryTableLoadFixture cold-start precedent.
        await Assertions.Expect(Page.GetByTestId("manga-cutoff-unmet-table"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // STATE assertion 3 (populated row): rowCount > 0 proves the cutoff-unmet
        // feed (GET /api/v5/manga/wanted/cutoff) actually returned the seeded
        // chapter — the predicate path through ChapterCutoffService +
        // ChapterRepository.ChaptersWhereCutoffUnmet (multi-lang profile + file
        // present + monitored) all hold.
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-cutoff-unmet-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(
            0,
            "SeedCutoffUnmetChapterAsync must produce >=1 below-cutoff chapter (multi-lang " +
            "TranslationProfile + ChapterFile row + Chapter.Monitored=true + ChapterFileId set)");

        // STATE assertion 4 (populated row state cells): every rendered row
        // exposes both current-quality + cutoff-quality cells per the Plan 18-06
        // testid spec. CutoffUnmetRow.tsx line 215 mounts -current-quality and
        // line 149 mounts -cutoff-quality. The populated path is the only path.
        for (var i = 0; i < rowCount; i++)
        {
            var row = rowsLocator.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-cutoff-unmet-row-", string.Empty);

            await Assertions.Expect(Page.GetByTestId($"manga-cutoff-unmet-row-{rowId}-current-quality")).ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId($"manga-cutoff-unmet-row-{rowId}-cutoff-quality")).ToBeVisibleAsync();
        }

        Page.Url.Should().EndWith("/manga/wanted/cutoffunmet");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — these are the FKs the SeedCutoffUnmetChapterAsync helper
    // needs. The chapter set is populated from the MangaDex /feed cassette
    // during the AddManga flow, so at least one chapter exists by the time
    // this runs. Mirrors the ResolveSeedFksAsync helpers in
    // HistoryRetryFixture / BlocklistBulkRemoveFixture verbatim.
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
