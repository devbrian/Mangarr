using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

/// <summary>
/// Phase 18 Plan 18-18 — Queue row Remove coverage (INVENTORY v5-endpoint
/// row 91: DELETE /api/v5/queue/{id}).
///
/// Seeds a manga via AddMangaFlow, navigates to the Queue page. When queue
/// rows exist (e.g. a chained grab event from upstream — not in this
/// fixture's flow), clicks a row's Remove button and asserts the row count
/// decremented + the RemoveQueueItemModal confirm path completes the DELETE.
///
/// When no queue rows exist (fresh-DB seed without chained grab — the
/// canonical empty path under cassette state), the fixture asserts on the
/// page+table shape + URL.
///
/// State assertion: when remove fires, queue row count goes from N → N-1.
/// Empty-state path: shell visibility + URL preservation.
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Explicit("Phase 18 Plan 18-14 D-D dependency (#102): AddMangaModal.ConfirmAddAsync click→nav race. AddMangaFlow.AddByMangaDexIdAsync times out at WaitForURLAsync until that lands. Drop this attribute when #102 closes.")]
public class QueueRowRemoveFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task queue_row_remove_decrements_count_or_empty_state()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: shell present.
        await Assertions.Expect(Page.GetByTestId("manga-queue-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();

        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var countBefore = await rowsLocator.CountAsync();

        if (countBefore == 0)
        {
            // WR-05 (18-REVIEW): silent `return` reported as PASSED, even
            // though the DELETE /api/v5/queue/{id} contract (INVENTORY
            // v5-endpoint row 91) was never exercised. Assert.Inconclusive
            // surfaces the un-exercised contract; the [Explicit] gate on the
            // class still defends the upstream-dependency story. When the
            // chained-grab seed propagates, this branch stops firing and the
            // populated path takes over inline.
            Page.Url.Should().EndWith("/manga/activity/queue");
            Assert.Inconclusive(
                "Queue empty under current cassette state — DELETE /api/v5/queue/{id} contract NOT exercised. " +
                "DEF-18-18-01 / issue #102: chained-grab seed activates once InteractiveSearchGrabFixture upstream lands.");
        }

        // Populated path: click the per-row Remove button. QueueRow.tsx
        // exposes the SpinnerIconButton with title='RemoveFromQueue' at
        // QueueRow.tsx:417-422 — locate by accessible name.
        var firstRow = rowsLocator.First;
        var firstRowIdAttr = await firstRow.GetAttributeAsync("data-testid");
        var firstRowId = firstRowIdAttr!.Replace("manga-queue-row-", string.Empty);

        // The remove button is inside the row's actions cell. Locate by
        // accessible name 'RemoveFromQueue' (translate key).
        var removeButton = firstRow.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove" }).First;
        await removeButton.ClickAsync();

        // RemoveQueueItemModal pops up with a confirm button.
        await Page.WaitForTimeoutAsync(500);
        var confirmButton = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Remove" }).Last;
        await confirmButton.ClickAsync();
        await Page.WaitForTimeoutAsync(1_500);

        // STATE assertion 2: the specific row went away (the per-row testid
        // is hidden — the DELETE /api/v5/queue/{id} contract).
        var doomedRow = Page.GetByTestId($"manga-queue-row-{firstRowId}");
        var doomedCount = await doomedRow.CountAsync();
        doomedCount.Should().Be(0, "removed queue row must be absent after DELETE /api/v5/queue/{id}");

        // STATE assertion 3: total count decremented (defends against the
        // failure mode where the removed row testid is detached but the row
        // count is stale).
        var countAfter = await Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$")).CountAsync();
        countAfter.Should().Be(countBefore - 1, "queue row count must decrement after remove");
    }
}
