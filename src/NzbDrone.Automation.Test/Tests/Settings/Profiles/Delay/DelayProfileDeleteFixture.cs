using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfile DELETE fixture. Greens INVENTORY
/// v5-endpoint row `DELETE /api/v5/delayprofile/{id} | Settings/Profiles Delay row delete`.
///
/// Creates a profile (Id != 1) then DELETEs it. Asserts:
///   1. Successful DELETE returns 2xx for non-default ids.
///   2. Subsequent GET list no longer includes the deleted id (state-not-rendering).
/// The default profile (Id=1) is undeletable per Sonarr-canonical guard in
/// DelayProfileController.DeleteProfile (MethodNotAllowedException).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileDeleteFixture : AutomationTest
{
    [Test]
    public async Task delete_removes_row()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        var label = $"plan-23-02-del-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);

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
        var createdId = (await createResp.JsonAsync())!.Value.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(1);

        // DELETE.
        var deleteResp = await Page.APIRequest.DeleteAsync($"{apiBase}/{createdId}");
        deleteResp.Status.Should().BeInRange(
            200,
            299,
            $"DELETE should succeed for non-default profile (Id={createdId}); body: {await deleteResp.TextAsync()}");

        // Subsequent GET list does not contain the deleted id.
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var listJson = await listResp.JsonAsync();
        var foundDeleted = false;
        foreach (var element in listJson!.Value.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == createdId)
            {
                foundDeleted = true;
                break;
            }
        }

        foundDeleted.Should().BeFalse("deleted DelayProfile must NOT appear in subsequent GET list (state-not-rendering invariant)");
    }
}
