using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-08 -- InteractiveSearchGrabFixture (D-14 PRSmoke).
//
// Asserts the full Grab -> History chained-system pipeline: clicking Grab on
// a release row pushes the grab through the backend pipeline (Plan 12
// MangaReleaseController.DownloadRelease -> MangaQueue -> SignalR push) and
// a history row materializes on /manga/activity/history.
//
// This is THE state-not-rendering regression catcher per memory
// feedback_verify_ui_state_not_just_rendering.md: a grab that 404s, returns
// silently, or fails to write history would leave the row visible AND the
// no-results placeholder absent -- a count-based assertion misses both. The
// chained-system history-row assertion proves the pipeline end-to-end.
//
// Currently marked [Explicit] for the same reasons as the Open fixture
// (Plan-04 AddMangaFlow dependency + Plan-05 manga-history-row testids).
// Promotes to live [Test] + [Category("PRSmoke")] when Plans 04/05 merge.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
[Explicit("Plan-04 dependency: AddMangaFlow + AddManga frontend testids. Plan-05 dependency: manga-history-row testids + MangaHistoryPage PageObject. Promotes to live PRSmoke when Plans 04 + 05 merge (Phase 18 Wave 2 integration).")]
public class InteractiveSearchGrabFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task grab_writes_to_history()
    {
        var slug = await SeedMangaAsync(KnownMangaDexId);

        await SearchAndGrabFlow.OpenForMangaAndGrabFirstReleaseAsync(Page, RootUri, slug);

        // STATE assertion (chained-system): a history row materializes after
        // the grab. The `manga-history-row-{id}` testid is Plan-05's
        // deliverable; the regex pattern matches the runtime emission.
        await Page.GotoAsync($"{RootUri}/manga/activity/history");

        var historyRows = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));

        // SignalR push may take a few seconds; allow up to 30s for the row.
        await Assertions.Expect(historyRows.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 30_000
        });
        var count = await historyRows.CountAsync();
        count.Should().BeGreaterThan(0, "grab must produce at least one history row");
    }

    /// <summary>
    /// Local AddManga seed -- mirrors the helper in InteractiveSearchOpenFixture.
    /// Replaced by AddMangaFlow.AddByMangaDexIdAsync when Plan-04 merges.
    /// </summary>
    private async Task<string> SeedMangaAsync(string mangaDexId)
    {
        await Page.GotoAsync($"{RootUri}/add/manga");
        var searchInput = Page.GetByRole(AriaRole.Textbox).First;
        await searchInput.FillAsync(mangaDexId);
        await searchInput.PressAsync("Enter");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
        {
            Timeout = 30_000
        });
        var addBtn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add" }).First;
        await addBtn.ClickAsync();
        await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"), new PageWaitForURLOptions
        {
            Timeout = 30_000
        });
        return Page.Url.Split('/').Last();
    }
}
