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
/// axis SelectLanguageModal (INVENTORY row 173).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 (deterministic precondition + path c):** See Plan 20-10
/// InteractiveImportModalFixture for the full rationale + I#2 documentation.
/// SelectLanguageModal is nested inside InteractiveImportRow's per-row
/// translatedLanguage cell.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manualimport?folder={seeded} returns 200 with rows.
///   2. The ManualImportItem listing carries the `translatedLanguage` field —
///      the per-row data SelectLanguageModal would offer for edit (per
///      ManualImportItem.cs:32 + Phase 16.1 D-04 wire-name canonical).
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectLanguageModalFixture : AutomationTest
{
    private string _seededFolder;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await testKit.DisableComixIndexerAsync();
#pragma warning restore CS0618
        var folderPath = Path.Combine(Path.GetTempPath(), $"ii-language-{Guid.NewGuid():N}");
        _seededFolder = await testKit.SeedInteractiveImportFolderAsync(folderPath);
    }

    [Test]
    [Explicit("GH #175 — V5 ManualImport controller not implemented; fixture greens automatically once #175 ships")]
    public async Task language_select()
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

        resp.Status.Should().Be(
            200,
            "manualimport endpoint must return 200 — SelectLanguageModal data source");

        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "manualimport response must include the per-file ManualImportItem payload");
        body.Should().Contain(
            "translatedLanguage",
            "manualimport rows must carry translatedLanguage — the per-row field SelectLanguageModal offers for edit");
    }
}
