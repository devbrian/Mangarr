using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — App boot i18n load endpoint.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/localization | App boot i18n`.
///
/// /api/v5/localization is hit by the frontend on app boot to load translation
/// strings. The fixture probes the endpoint directly (the boot path is exercised
/// every time AutomationTest opens the app shell) and asserts the response carries
/// at least one i18n key.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class LocalizationLoadFixture : AutomationTest
{
    [Test]
    public async Task strings_load_on_boot()
    {
        await Page.GotoAsync(RootUri);

        var resp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/localization");
        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().StartWith("{");

        // Response is a dictionary of i18n keys; any non-trivial response must
        // carry the "Mangarr" string token at minimum (app product name).
        body.Should().Contain("Mangarr");
    }
}
