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

    // Phase 33 (COMIX2-01): the #250 Explicit attribute is REMOVED — this fixture now
    // runs in the default-offline Replay suite. Per CONTEXT.md D-01 the #250 30s-timeout root
    // cause was Comix-not-disabled in the fan-out hanging the live signer (NOT a
    // MangaDex cassette gap); Plan 33-02's CassettingComixSigner + Plan 33-03's
    // recorded cassettes make the Comix fan-out resolve offline. Closes GH #250.
    [Test]
    public async Task interactive_search_opens_with_results_or_explicit_no_results()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Derive slug from the post-Add URL.
        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering): the
        // cassetting signer + MangaDex cassettes guarantee a results-bearing search —
        // at least one release row must render (the prior count==0 → no-results branch
        // existed only because the live Comix fan-out could time out; Phase 33 removes
        // that failure mode per CONTEXT.md D-01).
        var count = await modal.GetReleaseCountAsync();
        count.Should().BeGreaterThan(
            0,
            "Phase 33 D-11: cassetting signer + MangaDex cassettes guarantee at least 1 row");

        // STATE assertion (mixed-source): at least one row is sourced from Comix —
        // proves the offline Comix fan-out resolved (data-source is Plan 33-01's attr).
        var comixCount = await modal.ModalRoot.Locator("[data-source='Comix']").CountAsync();
        comixCount.Should().BeGreaterThan(
            0,
            "Phase 33 D-11: Comix indexer must render at least one row via cassetting signer + recorded cassette");

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
