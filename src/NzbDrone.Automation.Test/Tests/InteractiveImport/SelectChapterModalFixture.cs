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
/// axis SelectChapterModal (INVENTORY row 177: pending rename — GH #86).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 (deterministic precondition + path c):** See Plan 20-10
/// InteractiveImportModalFixture for the full rationale + I#2 documentation.
/// SelectChapterModal offers the `chapters` list (per ManualImportItem.cs:30
/// — a list of NzbDrone.Core.Manga.Chapter rows).
///
/// Note (INVENTORY row 177): the modal file is currently named
/// `SelectEpisodeModalContent.tsx` (pending rename per GH #86); the test
/// name uses the canonical `chapter_select` per INVENTORY (the manga-shape
/// covering-test name). When GH #86 ships the rename, the React file slug
/// catches up to the test contract.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/manualimport?folder={seeded} returns 200 with rows.
///   2. The ManualImportItem listing carries the `chapters` array — the
///      per-row data SelectChapterModal would offer for edit.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectChapterModalFixture : AutomationTest
{
    private string _seededFolder;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var testKit = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await testKit.DisableComixIndexerAsync();
        var folderPath = Path.Combine(Path.GetTempPath(), $"ii-chapter-{Guid.NewGuid():N}");
        _seededFolder = await testKit.SeedInteractiveImportFolderAsync(folderPath);
    }

    [Test]
    [Explicit("GH #175 — V5 ManualImport controller not implemented; fixture greens automatically once #175 ships")]
    public async Task chapter_select()
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
            "manualimport endpoint must return 200 — SelectChapterModal data source");

        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "manualimport response must include the per-file ManualImportItem payload");
        body.Should().Contain(
            "chapters",
            "manualimport rows must carry chapters array — the per-row field SelectChapterModal offers for edit");
    }
}
