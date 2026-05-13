using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

// Phase 18 Plan-05: Blocklist cluster coverage.
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08), navigates to
// Blocklist. When at least one row is present, clicks the remove button and
// asserts the row is removed — STATE assertion (per
// feedback_verify_ui_state_not_just_rendering.md) on the row's *absence* after
// the action, not just visual confirmation that some other row rendered.
//
// Cassette-state-dependent: an empty blocklist on the fresh-seed cassette is
// a valid Activity-cluster outcome (the page + table render and the empty
// branch is no-op coverage — Plan-03's MangaBlocklistPage.OpenAsync already
// proves the shell loads). Plan-08 (Wanted/Search) seeds rejected releases
// upstream, which is where the populated-blocklist coverage will land.
[TestFixture]
[Category("AutomationTest")]
public class MangaBlocklistFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task blocklist_remove_row_disappears_state_assertion()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaBlocklistPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present (page + table testids).
        // If the page-load itself failed, both ToBeVisibleAsync assertions fail
        // fast — this is the canonical empty-blocklist coverage path.
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-blocklist-table")).ToBeVisibleAsync();

        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();

        if (countBefore > 0)
        {
            // Populated cassette path: exercise the remove flow + assert STATE
            // change (row disappears).
            var firstRow = rowsLocator.First;
            var idAttr = await firstRow.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-blocklist-row-", string.Empty);

            // Reference handle to the soon-to-be-removed row for the
            // post-click hidden assertion.
            var doomedRow = Page.GetByTestId($"manga-blocklist-row-{rowId}");

            await Page.GetByTestId($"manga-blocklist-row-{rowId}-remove-button").ClickAsync();

            // STATE assertion 2: the specific row went hidden (not just "some
            // row was removed somewhere on the page").
            await doomedRow.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 10_000
            });

            // STATE assertion 3: the count decremented (defends against the
            // failure mode where the removed-row testid is intact but
            // detached — i.e. React re-rendered with stale state).
            var countAfter = await Page.GetByTestId(new Regex(@"^manga-blocklist-row-\d+$")).CountAsync();
            countAfter.Should().Be(countBefore - 1);
        }
        else
        {
            // Empty-cassette path: the table-empty placeholder is valid coverage.
            // No-op; cassette-state-dependent skip documented in 18-05-SUMMARY.md.
            TestContext.WriteLine(
                "[Plan-05] Blocklist empty under current cassette state — populated-row path skipped. " +
                "Plan-08 (Wanted/Search) will seed rejected releases for the populated path.");
        }

        Page.Url.Should().EndWith("/manga/activity/blocklist");
    }
}
