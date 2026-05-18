using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfile PUT (edit) fixture. Greens INVENTORY
/// v5-endpoint row `PUT /api/v5/delayprofile/{id} | Settings/Profiles Delay Edit modal`.
///
/// Round-trips httpDelay (5 -> 10) via PUT and asserts the updated value
/// returns on the next GET. State-not-rendering assertion per
/// feedback_verify_ui_state_not_just_rendering — assert the SPECIFIC httpDelay
/// value, not just "row count = N".
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileEditFixture : AutomationTest
{
    [Test]
    public async Task http_delay_roundtrips()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        // Seed a tag (non-default DelayProfile must have >=1 tag per Sonarr-canonical validator).
        var label = $"plan-23-02-edit-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);
        tagId.Should().BeGreaterThan(0);

        // POST: create profile with httpDelay=5.
        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                preferredProtocol = "http",
                httpDelay = 5,
                order = 0,
                bypassIfHighestQuality = false,
                bypassIfAboveCustomFormatScore = false,
                minimumCustomFormatScore = 0,
                tags = new[] { tagId }
            }
        });
        createResp.Status.Should().BeInRange(200, 299);
        var created = (await createResp.JsonAsync())!.Value;
        var createdId = created.GetProperty("id").GetInt32();

        // PUT: update httpDelay to 10.
        var putResp = await Page.APIRequest.PutAsync($"{apiBase}/{createdId}", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = createdId,
                preferredProtocol = "http",
                httpDelay = 10,
                order = 0,
                bypassIfHighestQuality = false,
                bypassIfAboveCustomFormatScore = false,
                minimumCustomFormatScore = 0,
                tags = new[] { tagId }
            }
        });
        putResp.Status.Should().BeInRange(
            200,
            299,
            $"PUT should succeed; received body: {await putResp.TextAsync()}");

        // GET single: assert httpDelay round-tripped to 10 (state-not-rendering specific value).
        var getResp = await Page.APIRequest.GetAsync($"{apiBase}/{createdId}");
        getResp.Status.Should().Be(200);
        var fetched = (await getResp.JsonAsync())!.Value;
        fetched.GetProperty("httpDelay").GetInt32().Should().Be(10,
            "PUT must update httpDelay; GET must return the updated value (state-not-rendering invariant)");
    }
}
