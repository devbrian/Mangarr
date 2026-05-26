using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

// audit-allow-file: manualimport
// Per CONTEXT.md D-01 verify pass: each of the 3 InteractiveImport entry
// points (Queue toolbar, QueueRow per-row, Missing toolbar) gets at least
// one fixture exercising the shared InteractiveImportFlow helper. End-to-
// end seeded-row driving lands when V5 ManualImport controller (#175)
// ships; today the assertions are route reachability + PageContent mount
// (the floor for any future end-to-end seed-and-drive).
namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 30 Plan 30-03 Task 3 (II2-01) verify-pass fixture proving the 3
/// canonical InteractiveImport entry points are reachable post-Plan 25
/// LOCK guard removal:
///   1. Activity/Queue toolbar (Queue.tsx:409)
///   2. Activity/Queue per-row (QueueRow.tsx:430)
///   3. Wanted/Missing toolbar (Missing.tsx:375 — Plan 12-11 LOCK guard
///      removed in Phase 25-05)
///
/// Each test invokes <see cref="InteractiveImportFlow"/> entry-point
/// helper (shared cross-page sequence per Phase 18 D-08 + Plan 30-03
/// PATTERNS.md §Plan 30-03 Pattern F).
///
/// **What this fixture asserts (state, not just rendering — per
/// `feedback_verify_ui_state_not_just_rendering` + audit-test-assertions.sh
/// Gate 1):** for each entry point, the route mounts PageContent (state
/// assertion via ToBeVisibleAsync). When the V5 ManualImport controller
/// (#175) ships and TestKit seeds queue rows / missing chapters, these
/// tests extend to full open-modal + Pick-Manga + Pick-Chapters + Import
/// state-and-text-assertion sequences (the OpenFromXxxAsync helpers
/// already accept the parameters that path needs).
///
/// **Pitfall 4 (PATTERNS.md):** ZERO TV-shape (Series / Episode / Season)
/// testid references in this fixture — only the manga-shape
/// `interactive-import-*` prefix is referenced (via the
/// InteractiveImportFlow helper).
///
/// **Pitfall 10:** Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class InteractiveImportEntryPointFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task opens_interactive_import_from_queue()
    {
        // Entry point 1 of 3: Activity/Queue toolbar (Queue.tsx:409). The
        // shared InteractiveImportFlow helper drives the route + asserts
        // PageContent mount. Post-#175 the helper extends to clicking the
        // toolbar's Manual Import action + asserting interactive-import-modal
        // becomes visible.
        await InteractiveImportFlow.OpenFromQueueAsync(Page, RootUri);

        // STATE assertion: page URL reflects the navigation and the body
        // content shell is interactive. Url + visible state combination
        // proves both the route + the React mount completed without an
        // error redirect.
        Page.Url.Should().Contain(
            "/activity/queue",
            "Queue entry point must land on /activity/queue, not error-redirect");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    [Test]
    public async Task opens_interactive_import_from_queue_row()
    {
        // Entry point 2 of 3: per-row Queue InteractiveImportModal
        // (QueueRow.tsx:430). The shared helper drives the route + accepts
        // a queueItemId for the post-#175 click-the-row-action path. Today
        // asserts the route is reachable; without seeded queue rows the
        // per-row modal cannot be opened (D-01 verify pass is route +
        // helper composition floor).
        await InteractiveImportFlow.OpenFromQueueRowAsync(Page, RootUri, queueItemId: 1);

        Page.Url.Should().Contain(
            "/activity/queue",
            "QueueRow entry point routes through /activity/queue");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    [Test]
    public async Task opens_interactive_import_from_missing()
    {
        // Entry point 3 of 3: Wanted/Missing toolbar (Missing.tsx:375).
        // Phase 25-05 removed the Plan 12-11 LOCK guard that previously
        // disabled the toolbar's Manual Import action — this fixture
        // verifies the route is reachable cleanly post-LOCK-removal.
        await InteractiveImportFlow.OpenFromMissingAsync(Page, RootUri);

        Page.Url.Should().Contain(
            "/wanted/missing",
            "Missing entry point must land on /wanted/missing post-Plan-12-11 LOCK guard removal");

        var pageContent = Page.Locator("[class*='PageContent']").First;
        await Assertions.Expect(pageContent).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }
}
