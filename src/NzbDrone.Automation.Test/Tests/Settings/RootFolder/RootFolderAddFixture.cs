using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Settings.RootFolder;

/// <summary>
/// Phase 30 Plan 30-02 (II2-08) — RootFolder add round-trip driven through
/// <see cref="SettingsFlow.AddRootFolderAsync"/>.
///
/// Pre-Phase-30 the helper carried <c>_ = folderPath;</c> — it clicked the Add Root
/// Folder button but never typed the path, then silently passed any test that called it.
/// Plan 30-02 drops the discard, annotates the AddRootFolderModal wrapper with the
/// <c>add-root-folder-modal</c> testid (Strategy A per 30-PATTERNS.md — does NOT touch
/// the shared FileBrowserModal infrastructure), and wires the helper through the inner
/// path-input + Ok button.
///
/// State assertion (audit-test-assertions.sh Gate 1): after the helper completes, GET
/// <c>/api/v5/rootfolder</c> and verify a row exists whose <c>path</c> equals the path
/// the helper was asked to add — proving the folderPath argument was actually honored
/// (a no-op helper would never POST the new path).
///
/// Note: the existing <c>Tests/Settings/RootFolderAddFixture.cs</c> (Plan 20-07b)
/// exercises POST <c>/api/v5/rootfolder</c> directly via Page.APIRequest — it is the
/// API-axis round-trip. This new fixture exercises the UI flow (button → FileBrowser
/// modal → path input → Ok click) end-to-end through the SettingsFlow helper.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RootFolderAddFixture : AutomationTest
{
    [Test]
    public async Task add_via_flow_helper_persists()
    {
        // Arrange — pick a fresh on-disk path; the baseline seed already claims
        // {AppData}/MangaLibrary, so we use a sibling that the RootFolder validator
        // will accept (must exist + must be writable).
        var folderPath = Path.Combine(Path.GetTempPath(), $"mangarr-30-02-rf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folderPath);

        // Act — invoke the helper. Pre-Plan-30-02 this would click the AddButton and
        // return without ever typing folderPath; post-Plan-30-02 it walks through the
        // FileBrowser modal annotated with add-root-folder-modal and clicks Ok.
        await SettingsFlow.AddRootFolderAsync(Page, RootUri, folderPath);

        // Assert — STATE assertion: GET /api/v5/rootfolder returns the new path.
        // Poll briefly to absorb any SignalR-refresh latency between the modal close
        // and the row being persisted to the DB.
        var body = string.Empty;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/rootfolder");
            listResp.Status.Should().Be(200);
            body = await listResp.TextAsync();
            if (body.Contains(folderPath.Replace("\\", "\\\\")))
            {
                break;
            }

            await Task.Delay(250);
        }

        body.Should().Contain(
            folderPath.Replace("\\", "\\\\"),
            $"the helper must have persisted folderPath to /api/v5/rootfolder; otherwise the folderPath arg is still being discarded");
    }
}
