using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

// audit-allow-file: manualimport
// V5 ManualImport controller deferred to v1.1 (gh #175 / gh #188). The
// `/api/v5/manualimport` references in this file describe the pending
// endpoint shape and will green automatically when the controller ships;
// fixture is gated with [Explicit] until then.
namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveImport modal sweep) — modal-action
/// axis SelectIndexerFlagsModal (INVENTORY row 172).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 (deterministic precondition + path c):** Seeds a folder via
/// SeedInteractiveImportFolderAsync so the manualimport scanner has rows;
/// asserts the wire-shape contract via Page.APIRequest direct-CRUD. The
/// SelectIndexerFlagsModal is nested inside InteractiveImportModalContent
/// and has no canonical UI entry point in v1 — Plan 20-04 / 20-07b / 20-08 /
/// 20-09 path c precedent.
///
/// **I#2 mechanically-identical-by-design rationale:** This fixture and its
/// 7 siblings (Select{Language,Quality,ReleaseGroup,ReleaseType,Chapter,Manga}Modal
/// + the parent InteractiveImportModal) all share the same shape per the
/// row-axis discipline. Documented in SUMMARY per I#2.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manualimport?folder={seeded} returns 200 with rows.
///   2. The ManualImportItem listing carries the `indexerFlags` field —
///      the per-row data the SelectIndexerFlagsModal would offer for edit.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectIndexerFlagsModalFixture : AutomationTest
{
    private string _seededFolder;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await testKit.DisableComixIndexerAsync();
        var folderPath = Path.Combine(Path.GetTempPath(), $"ii-flags-{Guid.NewGuid():N}");
        _seededFolder = await testKit.SeedInteractiveImportFolderAsync(folderPath);
    }

    [Test]
    [Explicit("GH #175 — V5 ManualImport controller not implemented; fixture greens automatically once #175 ships")]
    public async Task flags_select()
    {
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/manualimport?folder={Uri.EscapeDataString(_seededFolder)}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                },
                Timeout = 60_000
            });

        // STATE assertion 1: manualimport returns 200 for the seeded folder.
        resp.Status.Should().Be(
            200,
            "manualimport endpoint must return 200 — SelectIndexerFlagsModal data source");

        // STATE assertion 2: response carries the `indexerFlags` field on
        // each ManualImportItem (per ManualImportItem.cs:37 — IndexerFlags
        // is the field SelectIndexerFlagsModal would offer for selection).
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "manualimport response must include the per-file ManualImportItem payload");
        body.Should().Contain(
            "indexerFlags",
            "manualimport rows must carry indexerFlags — the per-row field SelectIndexerFlagsModal offers for edit");
    }
}
