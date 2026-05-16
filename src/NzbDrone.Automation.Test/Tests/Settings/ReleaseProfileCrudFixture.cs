using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07a (D-04 Nightly) — ReleaseProfile CRUD round-trip
/// fixture. Greens INVENTORY v5-endpoint row `GET /api/v5/releaseprofile |
/// Settings/Profiles ReleaseProfile CRUD`.
///
/// Blocker #4 mitigation: ReleaseProfile is NOT a ProviderControllerBase
/// descendant — it is a direct CRUD entity exposed at
/// <c>/api/v5/releaseprofile</c> (see
/// src/Mangarr.Api.V5/Profiles/Release/ReleaseProfileController.cs +
/// ReleaseProfileResource.cs). No TestKit seeder ships for it in Plan 20-01;
/// the fixture exercises the API contract directly via Playwright's
/// IAPIRequestContext (which inherits the X-Api-Key header from the browser
/// context — same pattern as DownloadClientsInProcessFixture).
///
/// CRUD round-trip: POST creates -> GET lists (contains new name) ->
/// DELETE removes -> GET no longer contains name. State-assertion at every
/// step.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ReleaseProfileCrudFixture : AutomationTest
{
    [Test]
    public async Task crud_roundtrip()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var profileName = $"Plan 20-07a RP {Guid.NewGuid():N}".Substring(0, 32);
        var apiBase = $"{RootUri}/api/v5/releaseprofile";

        // POST — create a new release profile.
        var createBody = new
        {
            name = profileName,
            enabled = true,
            required = new[] { "uncensored" },
            ignored = new[] { "raw" },
            airDateRestriction = false,
            airDateGracePeriod = 0,
            indexerIds = Array.Empty<int>(),
            tags = Array.Empty<int>(),
            excludedTags = Array.Empty<int>()
        };

        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = createBody
        });
        createResp.Status.Should().BeInRange(200, 299);

        var createdJson = await createResp.JsonAsync();
        createdJson.HasValue.Should().BeTrue();
        var createdId = createdJson!.Value.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(0);

        // GET — list contains the new profile by name.
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var listBody = await listResp.TextAsync();
        listBody.Should().Contain(profileName);

        // DELETE — remove the new profile.
        var deleteResp = await Page.APIRequest.DeleteAsync($"{apiBase}/{createdId}");
        deleteResp.Status.Should().BeInRange(200, 299);

        // GET — list no longer contains the profile name.
        var afterResp = await Page.APIRequest.GetAsync(apiBase);
        afterResp.Status.Should().Be(200);
        var afterBody = await afterResp.TextAsync();
        afterBody.Should().NotContain(profileName);
    }
}
