using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-08 -- InteractiveSearchOpenFixture.
//
// The plan-prescribed body called AddMangaFlow.AddByMangaDexIdAsync (a
// Plan-04 deliverable). In the parallel-worktree execution model this
// fixture executes in a worktree where Plan-04 has not yet merged; the
// AddManga seed therefore happens via a local helper that:
//   1. Navigates to /add/manga
//   2. Fills the search input with the manga title (test seed)
//   3. Clicks the result row's Add button
//   4. Waits for redirect to /manga/{slug}
//
// When Plan-04 merges into the integration branch, the canonical
// AddMangaFlow.AddByMangaDexIdAsync replaces SeedMangaAsync inline. The
// flow signature is intentionally kept narrow (Page + rootUri + slug-or-id)
// so the swap is mechanical.
//
// Currently marked [Explicit] because:
//   1. AddMangaFlow + AddManga frontend testids (Plan-04 deliverable) are
//      not present in this worktree
//   2. Cassette fixture data for MangaDex search is not yet seeded
//      (Plan-04 Fixtures/Cassettes/MangaDex/.gitkeep deliverable)
// Follow-up: GH issue (to be filed at phase close) tracks promotion to
// [Test] once Plan-04 lands.
[TestFixture]
[Category("AutomationTest")]
[Explicit("Plan-04 dependency: AddMangaFlow + AddManga frontend testids + MangaDex cassette fixtures. Promotes to [Test] when Plan-04 merges (Phase 18 Wave 2 integration).")]
public class InteractiveSearchOpenFixture : AutomationTest
{
    // Stable MangaDex ID for cassette-backed determinism (matches Plan-04
    // AddMangaFlowFixture seed).
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task interactive_search_opens_with_results_or_explicit_no_results()
    {
        var slug = await SeedMangaAsync(KnownMangaDexId);

        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering):
        // either at least one release row is rendered OR the explicit
        // no-results indicator is visible. Both are valid steady states;
        // the fixture fails only when neither is visible.
        var count = await modal.GetReleaseCountAsync();
        if (count == 0)
        {
            await Assertions.Expect(modal.NoResults).ToBeVisibleAsync();
        }
        else
        {
            // Each rendered row must expose its decision cell -- this is the
            // state-not-rendering target (the rejected-icon span is the
            // canonical example per Plan-08 Task 1 frontend annotations).
            var firstRow = Page.GetByTestId(new Regex(@"^interactive-search-row-[^-]+$")).First;
            var idAttr = await firstRow.GetAttributeAsync("data-testid");
            idAttr.Should().NotBeNull();
            var guid = idAttr!.Replace("interactive-search-row-", string.Empty);
            await Assertions.Expect(Page.GetByTestId("interactive-search-row-" + guid + "-decision"))
                            .ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// Local AddManga seed helper. Used until Plan-04's
    /// AddMangaFlow.AddByMangaDexIdAsync lands at merge. Returns the title
    /// slug from the post-add URL.
    /// </summary>
    private async Task<string> SeedMangaAsync(string mangaDexId)
    {
        await Page.GotoAsync($"{RootUri}/add/manga");

        // Plan-04 wires `add-manga-search-input` testid; until then, fall
        // back to a generic textbox role lookup.
        var searchInput = Page.GetByRole(AriaRole.Textbox).First;
        await searchInput.FillAsync(mangaDexId);
        await searchInput.PressAsync("Enter");

        // Wait for the result row + click its Add button. Plan-04 wires
        // testids; this fallback finds the first Add button.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
        {
            Timeout = 30_000
        });
        var addBtn = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add" }).First;
        await addBtn.ClickAsync();

        // Wait for navigation to /manga/{slug}. The slug is the path
        // segment after /manga/.
        await Page.WaitForURLAsync(new Regex(@"/manga/[^/]+$"), new PageWaitForURLOptions
        {
            Timeout = 30_000
        });
        return Page.Url.Split('/').Last();
    }
}
