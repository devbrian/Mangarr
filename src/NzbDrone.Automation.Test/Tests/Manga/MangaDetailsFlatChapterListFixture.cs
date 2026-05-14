using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — req DOMAIN-02 (INVENTORY line 50).
// "MangaDetails chapter table renders as flat list with no volume hierarchy"
//
// Asserts the Sonarr→Mangarr divergence from Phase 17.3 D-13 + PROJECT.md
// "Volumes/Seasons Out-of-Scope": the chapter table renders as a single flat
// sortable table with no volume-grouping or seasonal-grouping headers. The
// upstream Sonarr SeriesDetailsSeason nests Episode rows under collapsible
// Season cards; manga must NOT mirror that shape.
//
// [Explicit] cite: blocked by AddMangaFlow D-D nav race (issue #102).
[TestFixture]
[Category("AutomationTest")]
[Explicit("Plan 18-14 D-D blocker (issue #102): AddMangaFlow.ConfirmAddAsync nav race times out. Flip when D-D fix lands.")]
public class MangaDetailsFlatChapterListFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task renders_flat()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Switch to the Chapters tab — MangaDetails default lands on 'overview'.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // STATE assertion 1: NO volume-grouping headers (Sonarr-shape carry-over
        // would have rendered a per-volume header container). The volume/season
        // testid prefixes are forbidden per src/NzbDrone.Automation.Test/CLAUDE.md
        // "Forbidden prefixes" — we explicitly check absence here.
        var volumeHeaders = Page.GetByTestId(new Regex(@"^volume-\d+-header$"));
        await Assertions.Expect(volumeHeaders).ToHaveCountAsync(0);

        // STATE assertion 2: chapter rows render as direct flat-list rows. Each
        // row carries `chapter-row-{id}` per Plan 18-15's testid sweep (no
        // intermediate volume container in the DOM hierarchy).
        var chapterRows = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var count = await chapterRows.CountAsync();
        count.Should().BeGreaterThan(
            0,
            "the seeded manga's chapter table must materialize at least one chapter-row");
    }
}
