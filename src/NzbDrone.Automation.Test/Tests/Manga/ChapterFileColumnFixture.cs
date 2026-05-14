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
///
/// [Explicit] citation: tracks GH issue #102 (Plan 18-14 D-D — AddManga modal
/// nav race in ConfirmAddAsync).
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

        // The chapter rows render under MangaDetailsChapters → ChapterRow.tsx.
        // Each chapter row is keyed by chapterId. Wait for at least one chapter
        // row OR confirm an empty-chapters state — the cassette-seeded Komi
        // manga has chapters per Plan 18-14's recording-attempt evidence, so
        // we expect rows.
        //
        // ChapterRow does not yet have a data-testid annotation (no testid in
        // ChapterRow.tsx as of Wave 2). Use a Locator that matches any element
        // with a testid starting with "chapter-row-" — when the frontend
        // annotation sweep lands, this regex will match. Until then, fall back
        // to asserting the chapter section is present and visible (the
        // section's containing testid is what we anchor against).
        //
        // Since the chapter-table region rendering is the v5-endpoint contract
        // (GET /api/v5/chapterfile is fetched alongside GET /api/v5/chapter
        // when MangaDetails renders), we anchor on the visible-shell + URL.

        Page.Url.Should().MatchRegex(@"/manga/[^/]+$");

        // BL-05 (18-REVIEW): match only the row root, NOT descendant cells.
        // The companion fix on ChapterRow.tsx adds `chapter-row-{id}-file` to
        // the status (file) cell. A loose `data-testid^='chapter-row-'`
        // selector would now match BOTH the row AND every per-row file cell,
        // doubling the count and corrupting the per-row-file-cell loop. Anchor
        // on the row id by excluding the suffixed cells via an attribute
        // value regex.
        var chapterRows = Page.Locator("[data-testid^='chapter-row-']:not([data-testid$='-file']):not([data-testid$='-monitor-toggle'])");
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
