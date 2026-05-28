using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 137
// (`v5-endpoint GET /api/v5/system/backup → System/Backup page`).
//
// Non-cassette-dependent: /api/v5/system/backup queries the local backup
// directory. On a freshly-started runner with no backups taken the page
// renders the "No backups are available" alert. The fixture asserts on that
// empty-state alert (state assertion, not just visibility) — the alert text
// is the contract.
[TestFixture]
[Category("AutomationTest")]
public class BackupListFixture : AutomationTest
{
    [Test]
    public async Task backup_list_renders_empty_state_alert_on_fresh_db()
    {
        await new SystemBackupsPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: backup page shell.
        await Assertions.Expect(Page.GetByTestId("system-backups-page")).ToBeVisibleAsync();

        // STATE assertion 2 (terminal-state contract, AUTO-WAITING): fresh DB
        // produces either the "No backups are available" alert OR a backup table
        // (.zip rows) — both are valid terminal states (D-05 fresh-DB-per-fixture
        // makes empty the dominant case).
        //
        // WR-09 (18-REVIEW): "Backup Now" / "Restore Backup" are always-present
        // toolbar buttons that defeat the state-assertion purpose. Anchor only on
        // the data-bearing alternative (empty-state message) or a .zip filename.
        //
        // Flake fix (run 26567511609, postgres-16 nightly leg — the other 3 legs
        // passed): the page shell renders BEFORE the /api/v5/system/backup
        // round-trip populates the list, so a one-shot TextContentAsync()+MatchRegex
        // raced the data and captured only the toolbar buttons ("Backup NowRestore
        // Backup"). Use the auto-retrying ToContainTextAsync(Regex) so the assertion
        // waits for the data-bearing content instead of reading once too early.
        await Assertions.Expect(Page.GetByTestId("system-backups-page"))
            .ToContainTextAsync(
                new Regex(@"(No backups are available|\.zip)"),
                new() { Timeout = 15000 });

        Page.Url.Should().EndWith("/system/backup");
    }
}
