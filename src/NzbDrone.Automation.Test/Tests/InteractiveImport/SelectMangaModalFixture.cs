using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.InteractiveImport;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveImport modal sweep) — modal-action
/// axis SelectMangaModal (INVENTORY row 178: pending rename — GH #86).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 (deterministic precondition + path c):** See Plan 20-10
/// InteractiveImportModalFixture for the full rationale + I#2 documentation.
/// SelectMangaModal offers the `manga` reference (per ManualImportItem.cs:29
/// — `NzbDrone.Core.Manga.Manga` aggregate).
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manualimport?folder={seeded} returns 200 with rows.
///   2. The ManualImportItem listing carries the `manga` field — the
///      per-row data SelectMangaModal would offer for edit.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectMangaModalFixture : AutomationTest
{
    private string _seededFolder;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await testKit.DisableComixIndexerAsync();
        var folderPath = Path.Combine(Path.GetTempPath(), $"ii-manga-{Guid.NewGuid():N}");
        _seededFolder = await testKit.SeedInteractiveImportFolderAsync(folderPath);
    }

    [Test]
    [Explicit("GH #175 — V5 ManualImport controller not implemented; fixture greens automatically once #175 ships")]
    public async Task manga_select()
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
            "manualimport endpoint must return 200 — SelectMangaModal data source");

        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "manualimport response must include the per-file ManualImportItem payload");
        body.Should().Contain(
            "\"manga\"",
            "manualimport rows must carry the manga reference — the per-row field SelectMangaModal offers for edit");
    }
}
