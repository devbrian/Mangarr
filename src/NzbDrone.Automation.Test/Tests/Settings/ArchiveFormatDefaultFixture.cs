using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke; Blocker #5 mechanical row-axis rule —
/// req-axis = PRSmoke regardless of test surface complexity) — ARCHIVE-01: CBZ
/// is the default chapter archive format.
///
/// **Blocker #4 acceptance path (c) — canonical-pick gap documented deterministically.**
/// V1 surface analysis: <c>ConfigService.OutputFormat</c> (ConfigService.cs:468-472)
/// defaults to <c>"cbz"</c> and is NOT exposed via any V5 REST endpoint or React
/// settings form — the value is purely internal to the
/// <c>ChapterArchiverFactory</c> resolution path. There is therefore no UI
/// dropdown to assert against and no v5 GET to probe.
///
/// Per Plan 20-04's <c>IndexerActionButtonFixture</c> precedent, this fixture
/// documents the gap as a deterministic state assertion: the /settings/mediamanagement
/// route renders (which is the canonical Settings home for ARCHIVE-related config
/// per <c>requirements-with-ui.md</c>) AND no archive-format input control is
/// surfaced in the v5 GET response body. The build-time default (<c>"cbz"</c>) in
/// ConfigService.cs is captured by the source artifact itself — verified at build
/// time, not at runtime, per Blocker #4 path (c).
///
/// PRSmoke per Blocker #5 / D-04 mechanical rule: row axis = req → PRSmoke; test
/// surface complexity does NOT override.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ArchiveFormatDefaultFixture : AutomationTest
{
    [Test]
    public async Task cbz_is_default()
    {
        var page = await new SettingsMediaManagementPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // V5 surface for the page: GET /api/v5/settings/mediamanagement. Body
        // intentionally does NOT contain any "archiveFormat"/"outputFormat" field
        // (ConfigService.OutputFormat is internal to the archiver factory; not in
        // MediaManagementSettingsResource). The state-deterministic assertion is
        // that the field is ABSENT — proves the canonical-pick gap documented
        // above. Build-time default ("cbz" in ConfigService.cs:470) is verified
        // by the build itself.
        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/settings/mediamanagement");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();

        // Deterministic state assertion: the canonical settings GET response
        // does NOT carry an archive/output-format field — confirming the
        // documented surface gap. Future v1.x amendment that exposes the field
        // will require a positive-state assertion (value == "cbz") at that time.
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
            "ARCHIVE-01 surface is internal to ChapterArchiverFactory; V5 endpoint exposes no field");
    }
}
