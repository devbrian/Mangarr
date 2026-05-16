using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Components;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY v5-endpoint row
/// `GET /api/v5/filesystem` (FileBrowser entries).
///
/// W#8 SUMMARY rationale: D-04 tiers follow ROW AXIS (v5-endpoint → PRSmoke),
/// not test surface. FileBrowserFixture's modal-action trigger is incidental
/// to the row's v5-endpoint classification. The modal mount is the surface
/// that fires GET /api/v5/filesystem (per frontend/src/Path/usePaths.ts) but
/// the inventory row this fixture greens is the v5-endpoint axis.
///
/// Flow: open Settings/MediaManagement → AddRootFolder → modal opens with path
/// field → opening the modal triggers usePaths('/filesystem' base call).
///
/// The mechanical row-axis rule (Blocker #5 precedent from Plan 20-07b
/// ARCHIVE-01/02): TEST SURFACE COMPLEXITY does not override the axis-tier
/// mapping. v5-endpoint → PRSmoke regardless of whether the actual test path
/// hits a modal or a direct GET.
///
/// Blocker #4: deterministic precondition (Settings page-load + AddButton); zero
/// inconclusive-skip branches.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class FileBrowserFixture : AutomationTest
{
    [Test]
    public async Task entries_load()
    {
        var settings = await new SettingsRootFoldersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        // Race the GET /api/v5/filesystem response — the FileBrowser modal mounts
        // when AddRootFolder fires + usePaths('/filesystem') hook resolves.
        var fsTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/filesystem") && r.Request.Method == "GET",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await settings.AddButton.ClickAsync();

        // STATE assertion (v5-endpoint contract): GET /api/v5/filesystem returns 200.
        var resp = await fsTask;
        resp.Status.Should().Be(200,
            "GET /api/v5/filesystem must return 200 when the FileBrowser modal mounts");
        resp.Url.Should().Contain("/api/v5/filesystem",
            "request URL must hit the filesystem endpoint precisely");
    }
}
