using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 143
// (`v5-endpoint GET /api/v5/log → System/Events page`).
//
// Non-cassette-dependent: /api/v5/log returns the persisted event log from the
// running NzbDroneRunner. On the fresh-DB-per-fixture boot (D-05) the table
// reliably carries the lifecycle + default-seed messages — verified live
// (2026-05-28): "Application started", "Now listening on", "Seeding default
// indexer: Comix/MangaDex", "Setting up default translation/delay/custom-format
// profile". The fixture asserts the table surfaces one of those real log
// MESSAGES (data) OR the "No events found" empty-state alert as the terminal
// state — never chrome.
[TestFixture]
[Category("AutomationTest")]
public class EventsLogFixture : AutomationTest
{
    [Test]
    public async Task events_log_page_renders_terminal_state()
    {
        await new SystemEventsPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-events-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state-not-rendering): the page resolves into one of
        // two terminal states after the /api/v5/log query — either a real log
        // MESSAGE row is present (the dominant case after backend boot) or the
        // empty-state alert "No events found" renders.
        //
        // CodeRabbit (PR #293) correctly flagged the prior regex
        // (Refresh|Clear|Level|Logger|Time): those are toolbar/column-header chrome
        // that render with the shell, so the assertion could satisfy BEFORE the
        // /api/v5/log data arrived — a weak state assertion. Anchored instead on
        // deterministic boot/seed log MESSAGES (verified live 2026-05-28): the
        // lifecycle "Application started" plus the fresh-DB "Seeding default" /
        // "Setting up default" seed messages. (CodeRabbit suggested
        // "Mangarr started"/"Loading config" — those strings are NOT emitted; the
        // actual lifecycle/seed messages above are.)
        //
        // Flake-proofing (read-too-early class, run 26581903346): the auto-retrying
        // ToContainTextAsync(Regex) waits for the /api/v5/log content rather than
        // reading once before the table hydrates. Matches the DiskSpaceFixture guard.
        await Assertions.Expect(Page.GetByTestId("system-events-page"))
            .ToContainTextAsync(
                new Regex(@"(No events found|Application started|Seeding default|Setting up default)"),
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        Page.Url.Should().EndWith("/system/events");
    }
}
