using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfile POST (add) fixture. Greens INVENTORY
/// v5-endpoint row `POST /api/v5/delayprofile | Settings/Profiles Delay Add modal`.
///
/// Validator constraint per Sonarr-canonical port (DelayProfileController.cs
/// SharedValidator wiring): non-default profiles (Id != 1) MUST carry ≥1 tag.
/// The fixture seeds a Tag via the TestKit, then POSTs a DelayProfile carrying
/// that tag — both create and subsequent list-contains assertions run.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileAddFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Sonarr-canonical validator: non-default profiles must have ≥1 tag.
        var label = $"plan-23-02-add-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);
        tagId.Should().BeGreaterThan(0);

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        var createBody = new
        {
            preferredProtocol = "http",
            httpDelay = 5,
            order = 0,
            bypassIfHighestQuality = false,
            bypassIfAboveCustomFormatScore = false,
            minimumCustomFormatScore = 0,
            tags = new[] { tagId }
        };

        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = createBody
        });
        createResp.Status.Should().BeInRange(
            200,
            299,
            $"POST should succeed; received body: {await createResp.TextAsync()}");

        var createdJson = await createResp.JsonAsync();
        createdJson.HasValue.Should().BeTrue();
        var createdId = createdJson!.Value.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(1, "the Id=1 default is seeded; created Id must be 2+");

        // State assertion: list now contains the created profile (by id).
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var listJson = await listResp.JsonAsync();
        var listRoot = listJson!.Value;
        var foundCreated = false;
        foreach (var element in listRoot.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == createdId)
            {
                foundCreated = true;
                element.TryGetProperty("httpDelay", out var hd).Should().BeTrue();
                hd.GetInt32().Should().Be(5);
                break;
            }
        }

        foundCreated.Should().BeTrue("created DelayProfile must appear in GET list");
    }
}
