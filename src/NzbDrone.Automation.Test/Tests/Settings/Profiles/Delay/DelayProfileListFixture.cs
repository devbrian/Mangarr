using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfile GET (list) fixture. Greens INVENTORY
/// v5-endpoint row `GET /api/v5/delayprofile | Settings/Profiles Delay list`.
///
/// Asserts the default profile (Id=1, seeded by
/// DelayProfileService.Handle(ApplicationStartedEvent) per Pattern S4) is
/// present in the GET response. The route mounts at /settings/profiles
/// (DelayProfiles is a FieldSet within the Profiles page — there is NO
/// /settings/profiles/delay route at HEAD per 23-01-SUMMARY §Route mounting);
/// fixtures hit the API directly via Page.APIRequest.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileListFixture : AutomationTest
{
    [Test]
    public async Task list_returns_default_profile()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Microsoft.Playwright.Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200, "GET /api/v5/delayprofile should return 200 after Plan 23-02 controller port");

        var listJson = await listResp.JsonAsync();
        listJson.HasValue.Should().BeTrue();
        var root = listJson!.Value;
        root.ValueKind.Should().Be(global::System.Text.Json.JsonValueKind.Array);

        var foundDefault = false;
        foreach (var element in root.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == 1)
            {
                foundDefault = true;
                break;
            }
        }

        foundDefault.Should().BeTrue("the Pattern S4 seeder writes the Id=1 default DelayProfile on first ApplicationStartedEvent");
    }
}
