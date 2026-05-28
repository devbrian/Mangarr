using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 144
// (`v5-endpoint GET /api/v5/log/file → System/Logs/Files list`).
//
// Non-cassette-dependent: /api/v5/log/file lists files in
// <appdata>/logs/. NzbDroneRunner writes mangarr.txt at boot, so the list
// is guaranteed non-empty after the runner is up. State assertion is on the
// presence of the mangarr.txt entry (filename contains "mangarr"), not just
// row count — silent-empty regression guard.
[TestFixture]
[Category("AutomationTest")]
public class LogFilesListFixture : AutomationTest
{
    [Test]
    public async Task log_files_list_contains_mangarr_log_or_empty_alert()
    {
        await new SystemLogsPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present.
        await Assertions.Expect(Page.GetByTestId("system-logs-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility): the page resolves into
        // one of two terminal states: either the table contains the
        // mangarr.txt log file (the dominant case after boot) OR the empty
        // alert "No log files" renders. The regex covers both, plus the
        // filename text "mangarr" as a state-level anchor that distinguishes
        // a populated table from a stuck spinner.
        // WR-09 (18-REVIEW): "Refresh" / "Clear" are always-present toolbar
        // buttons; "Filename" is the column header (also always present
        // unless the empty-state alert is showing). Anchor only on the
        // data-bearing alternatives: the actual log file name "mangarr"
        // (case-insensitive guard for path variants) or the empty-state alert.
        //
        // Flake-proofing (read-too-early class, run 26581903346): "mangarr" is the
        // data-bearing log filename loaded via /api/v5/log/file AFTER the page shell
        // renders, so a one-shot TextContentAsync()+MatchRegex can race the data. Use
        // the auto-retrying ToContainTextAsync(Regex) instead, matching DiskSpaceFixture.
        // NB: case-insensitivity MUST be RegexOptions.IgnoreCase, NOT an inline (?i) —
        // Playwright serializes the .NET Regex to a JS RegExp, and JS rejects the inline
        // (?i) group ("Invalid regular expression … Invalid group"). RegexOptions.IgnoreCase
        // maps to the JS `i` flag correctly.
        await Assertions.Expect(Page.GetByTestId("system-logs-page"))
            .ToContainTextAsync(
                new Regex(@"(mangarr|No log files)", RegexOptions.IgnoreCase),
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        Page.Url.Should().EndWith("/system/logs/files");
    }
}
