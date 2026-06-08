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
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task toggle_persists()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

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

        // STATE assertion: aria-label flips after the PUT /api/v5/chapter/monitor
        // round-trip resolves AND React re-renders. WaitForLoadStateAsync(NetworkIdle)
        // proves the network is quiet, not that the toggled label has re-rendered, so a
        // direct GetAttributeAsync read races the re-render (GH #288: caught the stale
        // "Monitored, click to unmonitor" on the slow postgres-17 leg). The web-first
        // Expect polls until the label flips — a silent rejection would leave it unchanged
        // until the assertion times out.
        await Assertions.Expect(toggle).Not.ToHaveAttributeAsync("aria-label", beforeLabel!);

        var afterLabel = await toggle.GetAttributeAsync("aria-label");
        afterLabel.Should().NotBeNull();
        afterLabel.Should().NotBe(beforeLabel);
    }

    // Quick task 260608-mg8 — the Chapters-tab header cell above the per-row
    // bookmark column now hosts a monitor/unmonitor-ALL toggle
    // (chapter-table-monitor-all-toggle) wired to the SAME bulk endpoint
    // PUT /api/v5/chapter/monitor via useBulkToggleChaptersMonitored. This test
    // proves one click on the header toggle flips every chapter row's monitored
    // state (the bulk endpoint actually mutated state, not just re-rendered).
    [Test]
    public async Task toggle_all_flips_every_row()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        await Page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Chapters" })
                  .ClickAsync();

        await Page.GetByTestId("manga-details-chapter-table")
                  .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

        // The header monitor-all toggle lives in the (previously empty) header
        // cell above the per-row bookmark column.
        var headerToggle = Page.GetByTestId("chapter-table-monitor-all-toggle");
        await Assertions.Expect(headerToggle).ToBeVisibleAsync();

        var headerBefore = await headerToggle.GetAttributeAsync("aria-label");
        headerBefore.Should().NotBeNull();

        // Derive the first chapter-row id exactly as toggle_persists does.
        var rowsLocator = Page.GetByTestId(new Regex(@"^chapter-row-\d+$"));
        var rowCount = await rowsLocator.CountAsync();
        rowCount.Should().BeGreaterThan(0, "the seeded manga must have at least one chapter row");

        var firstRow = rowsLocator.First;
        var rowTestId = await firstRow.GetAttributeAsync("data-testid");
        rowTestId.Should().NotBeNullOrEmpty();
        var chapterId = rowTestId!.Replace("chapter-row-", string.Empty);

        var firstRowToggle = Page.GetByTestId($"chapter-row-{chapterId}-monitor-toggle");
        var rowBefore = await firstRowToggle.GetAttributeAsync("aria-label");
        rowBefore.Should().NotBeNull();

        await headerToggle.ClickAsync();

        // STATE assertion: web-first Expect polls through the bulk
        // PUT /api/v5/chapter/monitor round-trip + React re-render (mirrors the
        // toggle_persists GH#288 guard — a bare GetAttributeAsync read races the
        // re-render). The header aria-label flips AND the first row's toggle
        // aria-label flips to the opposite monitored state, proving the bulk
        // endpoint mutated chapter state (not merely re-rendered the icon).
        await Assertions.Expect(headerToggle).Not.ToHaveAttributeAsync("aria-label", headerBefore!);
        await Assertions.Expect(firstRowToggle).Not.ToHaveAttributeAsync("aria-label", rowBefore!);
    }
}
