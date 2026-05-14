using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 18 Plan 18-18 — ChapterFile column rendering on MangaDetails
/// (INVENTORY v5-endpoint row 82: GET /api/v5/chapterfile).
///
/// Seeds a manga via AddMangaFlow, navigates to the MangaDetails page, and
/// asserts that the chapter table renders with a ChapterFile column slot
/// for each chapter row. The slot may be empty (no file imported under the
/// fresh-DB seed) OR show a filename — both are valid states. The state
/// assertion is that the column rendering shape is intact (every row has a
/// file cell, not missing entirely) — without this, a frontend regression
/// that silently dropped the file column from the row template would pass
/// a naive "rows.Count > 0" check.
///
/// Per feedback_verify_ui_state_not_just_rendering: count-based assertions
/// hide silent-row-shape regressions. Per-row cell presence is the gate.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ChapterFileColumnFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task chapter_table_renders_file_column_for_each_chapter_row()
    {
        var details = await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // STATE assertion 1: MangaDetails page shell present.
        await Assertions.Expect(details.MainContainer).ToBeVisibleAsync();

        // Phase 19 fix-forward: MangaDetails lands on the 'overview' tab by
        // default — the chapter table only renders under the 'Chapters' tab.
        // The green analogs (MangaDetailsFlatChapterListFixture,
        // ChapterDetailsModalFixture) both click this tab before asserting on
        // chapter rows; this fixture omitted the step, so the chapter-row
        // selector matched 0 elements and the BL-05 silent-empty guard fired.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");

        // Phase 19 fix-forward: match ONLY the chapter-row root via the
        // `^chapter-row-\d+$` regex — the canonical pattern the green analogs
        // use. The prior `[data-testid^='chapter-row-']:not(...)` CSS chain
        // was a fragile attempt to exclude the per-row suffixed cells
        // (`-file`, `-monitor-toggle`, `-title`, `-search-button`); the
        // digits-anchored regex excludes every suffixed cell by construction.
        var chapterRows = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var rowCount = await chapterRows.CountAsync();

        // BL-05 (18-REVIEW): seeded manga MUST have at least one chapter row.
        // The fresh-DB Komi seed (KnownMangaDexId) is the canonical fixture
        // anchor; if rowCount is zero the cassette tier regressed and the
        // fixture must fail loudly (silent-empty is the bug class BL-05
        // closes).
        rowCount.Should().BeGreaterThan(0, "seeded manga must have at least one chapter row (BL-05 fix — silent-empty guard)");

        for (var i = 0; i < rowCount; i++)
        {
            var row = chapterRows.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("chapter-row-", string.Empty);

            // STATE assertion 3 (BL-05 closure): each chapter row exposes a
            // file cell per the GET /api/v5/chapterfile column contract.
            // Frontend annotation `chapter-row-{id}-file` lives on the status
            // (file) cell in ChapterRow.tsx; a regression that drops the cell
            // or rewrites the testid breaks this assertion immediately.
            var fileCell = Page.GetByTestId($"chapter-row-{rowId}-file");
            await Assertions.Expect(fileCell).ToBeVisibleAsync();
        }

        // STATE assertion 4: URL stability — confirms no spurious nav.
        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");
    }
}
