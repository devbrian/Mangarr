using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 135
// (`v5-endpoint GET /api/v5/diskspace → System Status disk usage`).
//
// Non-cassette-dependent: /api/v5/diskspace queries the local filesystem
// (Mangarr.Common DiskProvider). On any host with a mounted root the response
// is guaranteed non-empty, so the fixture asserts the rendered table contains
// byte-formatted size cells (the formatBytes() output uses suffixes B / KB /
// MB / GB / TB).
[TestFixture]
[Category("AutomationTest")]
public class DiskSpaceFixture : AutomationTest
{
    [Test]
    public async Task disk_space_renders_byte_formatted_cells()
    {
        await new SystemStatusPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell is the system-status-page testid.
        await Assertions.Expect(Page.GetByTestId("system-status-page")).ToBeVisibleAsync();

        // STATE assertion 2: the Disk Space FieldSet body contains a byte-
        // formatted size cell. We scope to the FieldSet (legend "Disk Space")
        // rather than the whole page so the regex doesn't drown in unrelated
        // tokens from Health/About/MoreInfo sections.
        //
        // `formatBytes` (frontend/src/Utilities/Number/formatBytes.ts) calls
        // `filesize({base: 2, round: 1})` which emits IEC binary units —
        // `B`/`KiB`/`MiB`/`GiB`/`TiB`. The regex accepts both the IEC binary
        // form (the actual current output) and the decimal SI form (defensive
        // against a future formatter swap).
        var diskSpaceSection = Page.Locator("fieldset:has(legend:has-text('Disk Space'))");
        await diskSpaceSection.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        // 2026-05-15 gh-152 round-2 fix: postgres-16 flaked once with the
        // FieldSet rendered but body empty ("Disk Space" was the entire
        // text content — table hadn't hydrated yet). Wait for the actual
        // disk-row content (table row cells with byte-formatted text)
        // before reading text so the regex isn't racing the /api/v5/diskspace
        // network round-trip on a slow runner. Auto-retrying assertion via
        // ToContainTextAsync handles the race naturally.
        await Assertions.Expect(diskSpaceSection)
            .ToContainTextAsync(
                new Regex(@"\d+(\.\d+)?\s*(B|[KMGT]i?B)"),
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var diskSpaceText = await diskSpaceSection.TextContentAsync();
        diskSpaceText.Should().NotBeNullOrEmpty();
        diskSpaceText.Should().MatchRegex(@"\d+(\.\d+)?\s*(B|[KMGT]i?B)");

        Page.Url.Should().EndWith("/system/status");
    }
}
