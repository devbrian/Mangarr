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

        // STATE assertion 2: page body text contains a byte unit (B / KB / MB /
        // GB / TB) — `formatBytes` output is the contract here. If the
        // /api/v5/diskspace call were to return an empty array (would only
        // happen on a host with no detectable drives — practically impossible),
        // this assertion catches the silent-empty regression that pure
        // visibility tests would miss.
        var pageBody = await Page.GetByTestId("system-status-page").TextContentAsync();
        pageBody.Should().NotBeNullOrEmpty();
        pageBody.Should().MatchRegex(@"\d+(\.\d+)?\s*(B|KB|MB|GB|TB)");

        Page.Url.Should().EndWith("/system/status");
    }
}
