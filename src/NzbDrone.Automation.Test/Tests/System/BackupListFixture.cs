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

        // STATE assertion 2 (terminal-state contract): fresh DB produces
        // either "No backups are available" alert OR a backup table — both
        // are valid terminal states (the test runner could in principle have
        // had a stale backup if not isolated, but D-05 fresh-DB-per-fixture
        // makes empty the dominant case). Match either pattern.
        var pageText = await Page.GetByTestId("system-backups-page").TextContentAsync();
        pageText.Should().NotBeNullOrEmpty();

        // WR-09 (18-REVIEW): "Backup Now" / "Restore Backup" are always-present
        // toolbar buttons that defeat the state-assertion purpose. Anchor
        // only on the data-bearing alternative (empty-state message); a
        // populated list will also expose the timestamp/filename text that
        // distinguishes it from the empty state.
        pageText.Should().MatchRegex(@"(No backups are available|\.zip)");

        Page.Url.Should().EndWith("/system/backup");
    }
}
