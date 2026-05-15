using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 Nightly) — MetadataSource Set-Primary endpoint.
/// Greens INVENTORY v5-endpoint row
/// `POST /api/v5/metadatasource/{id}/setprimary | Settings/MetadataSource Set-Primary`.
///
/// API-driven (Plan 20-07a ReleaseProfileCrudFixture precedent): the Set-Primary
/// surface is a backend-only contract; seeds two MetadataSource rows so a non-current
/// primary id is available, then hits the POST endpoint directly. Blocker #4 path b:
/// seeder guarantees both rows exist deterministically.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MetadataSourceSetPrimaryFixture : AutomationTest
{
    [Test]
    public async Task set_primary_persists()
    {
        // Seed two MetadataSource rows; the second becomes the SetPrimary target.
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedMetadataSourceAsync("Primary candidate A");
        var secondaryId = await tk.SeedMetadataSourceAsync("Primary candidate B");

        var page = await new SettingsMetadataSourcePage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var resp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/metadatasource/{secondaryId}/setprimary",
            new APIRequestContextOptions { DataObject = new { } });

        resp.Status.Should().BeInRange(200, 299);

        // State assertion: GET surface shows isPrimary=true on the just-set id.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/metadatasource");
        listResp.Status.Should().Be(200);
        var body = await listResp.TextAsync();

        using var doc = JsonDocument.Parse(body);
        var primary = false;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.GetProperty("id").GetInt32() == secondaryId &&
                el.TryGetProperty("isPrimary", out var ip) &&
                ip.GetBoolean())
            {
                primary = true;
                break;
            }
        }

        primary.Should().BeTrue("the just-promoted MetadataSource row should report isPrimary=true");
    }
}
