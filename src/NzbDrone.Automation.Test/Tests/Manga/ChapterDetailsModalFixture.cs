using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — modal-action ChapterDetailsModal
// (INVENTORY line 159). "MangaDetails chapter-row title click".
//
// Asserts the ChapterDetailsModal opens when a chapter title link is clicked
// in the MangaDetails Chapters tab. The modal is wrapped around an
// InteractiveSearch panel (search-first v1 simplification per Plan 07-05
// Lock #14); the modal header carries data-testid=chapter-details-modal-header
// per Plan 18-15's testid sweep.
//
// State assertion (per feedback_verify_ui_state_not_just_rendering): the
// modal-header testid materializes AND the InteractiveSearch surface inside
// the modal renders (interactive-search-modal testid). Both prove the modal
// body is functional, not just the shell.
//
// [Explicit] cite: blocked by AddMangaFlow D-D nav race (issue #102).
[TestFixture]
[Category("AutomationTest")]
[Explicit("Plan 18-14 D-D blocker (issue #102): AddMangaFlow.ConfirmAddAsync nav race times out. Flip when D-D fix lands.")]
public class ChapterDetailsModalFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task chapter_detail_renders()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Switch to the Chapters tab.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // Find a chapter row and click its title link. The title cell is an
        // anchor (ChapterTitleLink.tsx wraps it in Link) — clicking opens
        // the ChapterDetailsModal.
        var rows = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var rowCount = await rows.CountAsync();
        rowCount.Should().BeGreaterThan(0, "the seeded manga must have at least one chapter row");

        // ChapterTitleLink renders a Link element wrapping the title text;
        // a role-based click on the link text picks it up.
        var firstRow = rows.First;
        var titleLink = firstRow.Locator("a").First;
        await Assertions.Expect(titleLink).ToBeVisibleAsync();
        await titleLink.ClickAsync();

        // STATE assertion 1: the modal header materializes — Plan 18-15
        // testid sweep added chapter-details-modal-header to the modal
        // ModalHeader's child span.
        var modalHeader = Page.GetByTestId("chapter-details-modal-header");
        await Assertions.Expect(modalHeader).ToBeVisibleAsync(new()
        {
            Timeout = 10_000
        });

        // STATE assertion 2: the modal header text is non-empty (real
        // chapter metadata flowed in via useSingleChapter, not just a
        // placeholder shell).
        var headerText = await modalHeader.TextContentAsync();
        headerText.Should().NotBeNullOrWhiteSpace(
            "the ChapterDetailsModal header must render real chapter metadata, not an empty placeholder");

        // STATE assertion 3: the InteractiveSearch surface inside the modal
        // renders (search-first v1 shape per Plan 07-05). The
        // interactive-search-modal testid resolves both in the standalone
        // tab context and inside the modal wrapper per Plan-08 Task 1
        // annotations.
        await Assertions.Expect(Page.GetByTestId("interactive-search-modal").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }
}
