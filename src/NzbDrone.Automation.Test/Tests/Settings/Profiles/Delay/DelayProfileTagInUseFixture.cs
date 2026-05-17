using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfileTagInUseValidator integration fixture.
/// Greens INVENTORY req row `DP-03 | DelayProfileTagInUseValidator blocks
/// duplicate-tag profile`.
///
/// Cross-row tag uniqueness invariant (per Phase 22 D-03 live consumer #2):
///   1. Create profile A with `tags=[tagN]` -> 2xx.
///   2. Create profile B with `tags=[tagN]` -> 4xx (FluentValidation rejection).
///
/// State-not-rendering: asserts the SPECIFIC 4xx status, not just
/// "second create did not return 200".
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileTagInUseFixture : AutomationTest
{
    [Test]
    public async Task duplicate_tag_blocked()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var sharedTag = await tk.SeedTagAsync($"plan-23-02-tu-{Guid.NewGuid():N}".Substring(0, 24));

        // First create — succeeds.
        var firstResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                preferredProtocol = "http",
                httpDelay = 5,
                order = 0,
                bypassIfHighestQuality = false,
                bypassIfAboveCustomFormatScore = false,
                minimumCustomFormatScore = 0,
                tags = new[] { sharedTag }
            }
        });
        firstResp.Status.Should().BeInRange(200, 299, "first create with unique tag must succeed");

        // Second create with the SAME tag — must be rejected by DelayProfileTagInUseValidator.
        var secondResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                preferredProtocol = "http",
                httpDelay = 6,
                order = 0,
                bypassIfHighestQuality = false,
                bypassIfAboveCustomFormatScore = false,
                minimumCustomFormatScore = 0,
                tags = new[] { sharedTag }
            }
        });

        secondResp.Status.Should().BeInRange(
            400,
            499,
            $"second create with duplicate tag must be rejected by DelayProfileTagInUseValidator (Phase 22 D-03 live consumer #2). " +
            $"Received status {secondResp.Status}; body: {await secondResp.TextAsync()}");
    }
}
