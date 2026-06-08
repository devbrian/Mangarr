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

// Phase 18 Plan-05: Blocklist cluster coverage (per-row remove path).
//
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08), then seeds a real
// MangaBlocklist row via TestKit.SeedBlocklistAsync (Plan 19-01) — mirroring
// the BlocklistBulkRemoveFixture (Plan 19-05) precedent. With the seeded row
// in place, navigates to Blocklist, clicks the per-row remove button, and
// asserts the row is removed — STATE assertion (per
// feedback_verify_ui_state_not_just_rendering.md) on the row's *absence*
// after the action, not just visual confirmation that some other row rendered.
//
// gh #115 fix (2026-05-14): the previous version relied on cassette state for
// blocklist contents and treated an empty blocklist as "valid coverage" — but
// Blocklist.tsx only mounts the `manga-blocklist-page` / `manga-blocklist-table`
// testid wrappers inside the `records.length > 0` branch, so the shell
// ToBeVisibleAsync assertions at lines 38-39 of the prior version were
// unsatisfiable on empty (the empty-state Alert is a peer sibling with no
// shell testid). Switched to the Plan 19-05 seed-then-assert pattern so the
// fixture deterministically exercises the populated path. This fixture
// differs from BlocklistBulkRemoveFixture by exercising the per-row DELETE
// (DELETE /api/v5/manga/blocklist/{id}) rather than the bulk-DELETE
// (DELETE /api/v5/manga/blocklist/bulk).
[TestFixture]
[Category("AutomationTest")]
public class MangaBlocklistFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task blocklist_remove_row_disappears_state_assertion()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // gh #115: seed a real MangaBlocklist row via the raw-SQLite TestKit
        // helper (Plan 19-01 verdict, mirroring Plan 19-05's
        // BlocklistBulkRemoveFixture). Resolve the AddMangaFlow-seeded manga +
        // one of its chapters as the FKs, then INSERT into the backend's
        // per-fixture mangarr.db BEFORE navigating. Without this, the
        // cassette returns an empty blocklist and Blocklist.tsx renders the
        // empty-state Alert (peer-level, no shell testid) instead of the
        // testid-wrapped table — making the shell assertions below
        // unsatisfiable.
        var (mangaId, chapterId) = await ResolveSeedFksAsync();
        var testKit = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, Runner.AppData, Runner.PostgresOptions);
        await testKit.SeedBlocklistAsync(Runner.AppData, mangaId, chapterId);

        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present (page + table testids). With the
        // seed in place above, the populated branch in Blocklist.tsx renders
        // these wrappers; if the page-load itself failed or the seed silently
        // produced zero rows, both assertions fail fast.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();

        // STATE assertion 2: the seed produced at least one blocklist row. A
        // silent seed failure (e.g. a schema drift in MangaBlocklist) fails
        // the test loudly here rather than skipping past an empty page (the
        // pre-gh-#115 behaviour).
        countBefore.Should().BeGreaterThan(0, "seed must have produced a blocklist row");

        // Populated path: exercise the per-row remove flow + assert STATE
        // change (row disappears).
        var firstRow = rowsLocator.First;
        var idAttr = await firstRow.GetAttributeAsync("data-testid");
        var rowId = idAttr!.Replace("manga-blocklist-row-", string.Empty);

        // Reference handle to the soon-to-be-removed row for the
        // post-click hidden assertion.
        var doomedRow = Page.GetByTestId($"manga-blocklist-row-{rowId}");

        await Page.GetByTestId($"manga-blocklist-row-{rowId}-remove-button").ClickAsync();

        // STATE assertion 3: the specific row went hidden (not just "some
        // row was removed somewhere on the page").
        await doomedRow.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 10_000
        });

        // STATE assertion 4: the count decremented (defends against the
        // failure mode where the removed-row testid is intact but
        // detached — i.e. React re-rendered with stale state).
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$")).CountAsync();
        countAfter.Should().Be(countBefore - 1);

        Page.Url.Should().EndWith("/manga/activity/blocklist");
    }

    // Resolve the AddMangaFlow-seeded manga id + one of its chapter ids via
    // the V5 API — these are the FKs the raw-SQLite seed helper needs. The
    // chapter set is populated from the MangaDex /feed cassette during the
    // AddManga flow, so at least one chapter exists by the time this runs.
    // Mirrors BlocklistBulkRemoveFixture.ResolveSeedFksAsync (Plan 19-05).
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
