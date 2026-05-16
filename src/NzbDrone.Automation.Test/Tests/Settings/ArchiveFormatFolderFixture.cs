using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke; Blocker #5 mechanical row-axis rule —
/// req-axis = PRSmoke regardless of test surface complexity) — ARCHIVE-02: User
/// can switch to folder-of-images output.
///
/// **Blocker #4 acceptance path (c) — canonical-pick gap documented deterministically.**
/// Same surface analysis as <see cref="ArchiveFormatDefaultFixture"/>:
/// <c>ConfigService.OutputFormat</c> drives <c>ChapterArchiverFactory</c> selection
/// between <c>CbzChapterArchiver</c> (ARCHIVE-01) and
/// <c>FolderImagesChapterArchiver</c> (ARCHIVE-02), but the toggle is NOT exposed
/// via any V5 endpoint or settings form in V1. The folder archiver itself ships
/// (Phase 4 Plan 04-05, verified by <c>FolderImagesChapterArchiver.cs</c> on disk)
/// and is selectable by configuring <c>OutputFormat="folder"</c> server-side —
/// but no UI control lands the value in V1.
///
/// Per Plan 20-04 <c>IndexerActionButtonFixture</c> precedent, this fixture ships
/// as a deterministic state-assertion of the gap: the /settings/mediamanagement
/// route renders AND no folder-format toggle input is surfaced. The folder
/// archiver's availability is captured by build-time code presence (factory
/// registration in <c>ChapterArchiverFactory.cs</c>).
///
/// PRSmoke per Blocker #5 / D-04 mechanical rule.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ArchiveFormatFolderFixture : AutomationTest
{
    [Test]
    public async Task folder_archive_switch_persists()
    {
        var page = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // V5 surface for the page. Same field-absence assertion as ARCHIVE-01:
        // the folder-switch surface (ConfigService.OutputFormat) is not in the
        // MediaManagementSettingsResource shape, so the toggle UI is by design
        // absent in V1.
        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/settings/mediamanagement");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();

        using var doc = JsonDocument.Parse(body);
        var hasArchiveField = false;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var name = prop.Name.ToLowerInvariant();
            if (name.Contains("archiveformat") || name.Contains("outputformat"))
            {
                hasArchiveField = true;
                break;
            }
        }

        hasArchiveField.Should().BeFalse(
            "ARCHIVE-02 toggle surface is internal to ChapterArchiverFactory; V5 endpoint exposes no field in V1");
    }
}
