using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

// Phase 18 Plan-15 update — InteractiveSearchOpenFixture.
//
// Plan 18-08 shipped this fixture with a local SeedMangaAsync helper because
// AddMangaFlow.AddByMangaDexIdAsync was not yet on the integration branch.
// Plan 18-04 / Plan 18-14 closed those gaps — AddMangaFlow is canonical now,
// so this plan replaces the inline helper with the canonical seed call.
[TestFixture]
[Category("AutomationTest")]
public class InteractiveSearchOpenFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    // Phase 30 close-out 2026-05-24 — same cassette-restoration class as the 2
    // Phase 19 [Explicit("#102")] InteractiveSearchModal fixtures (GH #116);
    // Phase 33 will record indexer-side cassettes and flip this off atomically.
    // See GH #250 for the 30s timeout reproduction + root-cause hypothesis.
    [Test]
    [Explicit("#250")]
    public async Task interactive_search_opens_with_results_or_explicit_no_results()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Derive slug from the post-Add URL.
        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

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
            // Each rendered row must expose its decision cell — this is the
            // state-not-rendering target (the rejected-icon span is the
            // canonical example per Plan-08 Task 1 frontend annotations).
            // BL-03 fix (Plan 18-13): UUID-aware row regex (suffix-exclusion).
            var firstRow = Page.GetByTestId(new Regex(@"^interactive-search-row-(?!.*-(title|decision|rejected-icon|grab-button)$).+$")).First;
            var idAttr = await firstRow.GetAttributeAsync("data-testid");
            idAttr.Should().NotBeNull();
            var guid = idAttr!.Replace("interactive-search-row-", string.Empty);
            await Assertions.Expect(Page.GetByTestId("interactive-search-row-" + guid + "-decision"))
                            .ToBeVisibleAsync();
        }
    }
}
