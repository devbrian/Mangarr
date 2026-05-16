using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `GET /api/v5/command` (Command queue stream).
///
/// The frontend's useCommands hook polls GET /api/v5/command for live command
/// status — fires automatically on every page load (used by header buttons,
/// queue badge, settings refreshes). This fixture races the response on
/// the root URL load, asserting the v5-endpoint contract.
///
/// Per D-04 row-axis: v5-endpoint → PRSmoke. The GET is the read-shape of the
/// command queue stream — falls in PRSmoke per the mechanical rule.
///
/// Blocker #4: page-level seed only (no AddManga needed); zero
/// inconclusive-skip branches.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class CommandStreamFixture : AutomationTest
{
    [Test]
    public async Task commands_stream()
    {
        // Race a GET /api/v5/command response BEFORE the page-level navigation
        // that triggers it. The AutomationTest base already navigated to RootUri
        // in OneTimeSetUp, but useCommands polls every ~5s — wait for the next
        // tick by triggering a re-navigation.
        var cmdTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/command") &&
                 r.Request.Method == "GET" &&
                 !r.Url.Contains("/api/v5/command/"),
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await Page.GotoAsync(RootUri);

        // STATE assertion (v5-endpoint contract): GET /api/v5/command returns 200.
        var resp = await cmdTask;
        resp.Status.Should().Be(200,
            "GET /api/v5/command must return 200 (command queue stream contract)");
        resp.Url.Should().Contain("/api/v5/command",
            "request URL must hit the command endpoint precisely");
    }
}
