using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.V11Closeout;

// Phase 28 Plan 28-01 Task 2 — V11Closeout per-vertical fixture.
//
// Vertical 2: DelayProfile (Phase 23 schema-trim + V5 controller port).
// Validates Phase 23 Migration 002 trim: the DelayProfile resource MUST NOT
// expose the 4 dropped Sonarr-shape columns (enableUsenet, enableTorrent,
// usenetDelay, torrentDelay). Only HttpDelay + PreferredProtocol + Tags +
// Order + BypassIfHighestQuality + BypassIfAboveCustomFormatScore +
// MinimumCustomFormatScore remain. State assertion = schema shape, not bare
// rendering.
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileClosingFixture : AutomationTest
{
    [Test]
    public async Task delayprofile_v5_schema_excludes_phase_23_trimmed_columns()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/delayprofile");
        listResp.Status.Should().Be(200, "GET /api/v5/delayprofile after Phase 23 controller port");

        var json = await listResp.JsonAsync();
        json.HasValue.Should().BeTrue();
        var root = json!.Value;
        root.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);
        root.GetArrayLength().Should().BeGreaterThan(0,
            "default DelayProfile Id=1 is seeded by ApplicationStartedEvent handler");

        var first = root.EnumerateArray().GetEnumerator();
        first.MoveNext().Should().BeTrue();
        var defaultProfile = first.Current;

        // Pattern κ — Phase 23 trim: these 4 fields MUST be absent from the resource shape.
        defaultProfile.TryGetProperty("enableUsenet", out _).Should().BeFalse(
            "Phase 23 Migration 002 dropped enableUsenet column");
        defaultProfile.TryGetProperty("enableTorrent", out _).Should().BeFalse(
            "Phase 23 Migration 002 dropped enableTorrent column");
        defaultProfile.TryGetProperty("usenetDelay", out _).Should().BeFalse(
            "Phase 23 Migration 002 dropped usenetDelay column");
        defaultProfile.TryGetProperty("torrentDelay", out _).Should().BeFalse(
            "Phase 23 Migration 002 dropped torrentDelay column");

        // The trimmed-shape positive assertions: HttpDelay survives.
        defaultProfile.TryGetProperty("httpDelay", out _).Should().BeTrue(
            "HttpDelay is the only delay column manga shape retains (Phase 23 D-NN)");
    }
}
