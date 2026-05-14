using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — req PIPELINE-05 second clause + v5-endpoint
// PUT /api/v5/chapter/monitor (INVENTORY line 59 second clause + line 80).
//
// Per-chapter monitor toggle from the MangaDetails chapters tab. The toggle is
// the leftmost cell of every ChapterRow (frontend/src/Manga/Details/ChapterRow.tsx)
// and dispatches via useToggleChapterMonitored → PUT /api/v5/chapter/monitor.
[TestFixture]
[Category("AutomationTest")]
public class ChapterMonitorToggleFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task toggle_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Switch to the Chapters tab (MangaDetails default may be 'overview').
        // The tab is keyed by role="tab" + visible name "Chapters" per
        // MangaDetails.tsx TABS array.
        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        // Wait for the chapter table to render. The wrapper carries
        // data-testid="manga-details-chapter-table" per Plan 18-15 testid sweep.
        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // Find the first chapter-row. Each row carries `chapter-row-{id}` per the
        // testid sweep in this plan; its monitor-toggle child carries
        // `chapter-row-{id}-monitor-toggle`.
        var rowsLocator = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "the seeded manga must have at least one chapter row");

        var firstRow = rowsLocator.First;
        var rowTestId = await firstRow.GetAttributeAsync("data-testid");
        rowTestId.Should().NotBeNullOrEmpty();
        var chapterId = rowTestId!.Replace("chapter-row-", string.Empty);

        var toggle = Page.GetByTestId($"chapter-row-{chapterId}-monitor-toggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync();

        var beforeLabel = await toggle.GetAttributeAsync("aria-label");
        beforeLabel.Should().NotBeNull();

        await toggle.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var afterLabel = await toggle.GetAttributeAsync("aria-label");
        afterLabel.Should().NotBeNull();

        // STATE assertion: aria-label flips after the PUT /api/v5/chapter/monitor
        // round-trip resolves. A silent rejection would leave the label unchanged.
        afterLabel.Should().NotBe(beforeLabel);
    }
}
