using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Activity;

// Phase 18 Plan-05: Queue cluster coverage.
// Seeds a manga via the AddMangaFlow (Plan-04 product, D-08 — UI-populates-via-UI)
// then navigates to the Queue page and asserts on STATE (decision/status cell
// presence on every visible row) per feedback_verify_ui_state_not_just_rendering.md.
//
// The fixture is cassette-state-dependent — INVENTORY.md row 'route /manga/activity/queue'
// flips to 🟢 either way (an empty Queue exercises the page-load + empty-alert
// path; a non-empty Queue exercises the row + status-cell state assertions).
// Both paths are valid Activity-cluster coverage; the silent-rejection check is
// enforced inside the rendered-row branch.
[TestFixture]
[Category("AutomationTest")]
public class MangaQueueFixture : AutomationTest
{
    // MangaDex UUID for "Solo Leveling: Ragnarok" — stable cassette anchor per
    // Plan-04. Phase 16-06 SUMMARY confirms this manga has 68 chapters / 194
    // releases on MangaDex, so the cassette tier is deterministic. The exact
    // chapter set that appears in the Queue depends on cassette + cutover state;
    // the fixture only asserts on row shape, not row count.
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task queue_page_renders_table_with_state_assertions_on_visible_rows()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaQueuePage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page + table containers present (sane shell).
        await Assertions.Expect(Page.GetByTestId("manga-queue-page")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("manga-queue-table")).ToBeVisibleAsync();

        // STATE assertion 2 (per feedback_verify_ui_state_not_just_rendering.md):
        // if ANY rows render, EACH row exposes its status cell — silent-empty-row
        // rendering would fail this check. The selector targets row containers
        // (testids of the form `manga-queue-row-{id}` with no trailing -{cell}).
        var rowsLocator = Page.GetByTestId(new Regex(@"^manga-queue-row-\d+$"));
        var count = await rowsLocator.CountAsync();
        for (int i = 0; i < count; i++)
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
}
