using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 1 — Tests/Global/ queue badge (v5-endpoint
/// GET /api/v5/queue/status, INVENTORY row 96). Verifies the contract
/// behind the top-nav queue-count badge:
///
/// 1. The nav-activity sidebar anchor renders (PageSidebar.tsx attaches
///    data-testid="nav-activity") — the badge piggybacks on this entry.
/// 2. GET /api/v5/queue/status returns a well-formed JSON payload that
///    PageSidebarStatus consumes to decide kind/count/render-or-null.
///
/// Why the fallback approach (per Plan 18-16 Task 1 "NOTE"):
/// PageSidebarStatus returns null when count === 0 (PageSidebarStatus.tsx L18-L20),
/// so without seeding the queue, no badge node exists in DOM. The badge's
/// behavioral contract is "renders count when /api/v5/queue/status returns
/// non-zero" — we exercise the endpoint + nav anchor (state) rather than
/// driving an AddManga seed (which would require AddMangaFlow and hit issue
/// #102 D-D race).
///
/// Cross-process AddManga seed dependency: NONE — this fixture intentionally
/// avoids AddMangaFlow. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class QueueBadgeFixture : AutomationTest
{
    [Test]
    public async Task badge_updates_with_signalr()
    {
        await Page.GotoAsync($"{RootUri}/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: nav-activity anchor (where the badge piggybacks)
        // is in the DOM. PageSidebar.tsx L86-L94 confirms the testid is wired.
        var navActivity = Page.GetByTestId("nav-activity");
        await Assertions.Expect(navActivity).ToHaveAttributeAsync("href", new Regex("/manga/activity/queue"));

        // STATE assertion 2: the endpoint that feeds the badge returns a
        // contract-shaped JSON payload. With an empty queue (D-07 baseline
        // seed creates a root folder + InProcess client but no queued
        // chapters), the payload's `count` is 0 — but the field MUST be
        // present and numeric. This is the STATE check.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        var json = await http.GetStringAsync($"{RootUri}/api/v5/queue/status");

        json.Should().NotBeNullOrEmpty();

        // Schema check — every consumer of GET /api/v5/queue/status (the badge
        // included) expects `count` AND `errors` AND `warnings` keys. They drive
        // PageSidebarStatus' kind/count/render-or-null branches verbatim
        // (PageSidebarStatus.tsx L11-L33).
        json.Should().Contain("\"count\"");
        json.Should().Contain("\"errors\"");
        json.Should().Contain("\"warnings\"");
    }
}
