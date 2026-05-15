using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 Nightly) — RemotePathMapping CRUD round-trip via
/// Page.APIRequest. Greens INVENTORY v5-endpoint row
/// `GET /api/v5/remotepathmapping | RemotePathMapping CRUD`.
///
/// API-driven (Plan 20-07a ReleaseProfileCrudFixture precedent): RemotePathMapping
/// has no TestKit seeder and no per-row UI selectors landed in this plan's scope.
/// The CRUD exercise validates the v5 contract (POST → GET → DELETE).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class RemotePathMappingCrudFixture : AutomationTest
{
    [Test]
    public async Task crud_roundtrip()
    {
        // Open the DownloadClients page (RemotePathMappings live there).
        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // POST a new mapping.
        var postResp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/remotepathmapping",
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    host = "test-host.example",
                    remotePath = "/remote/data/",
                    localPath = "/local/data/"
                }
            });

        postResp.Status.Should().BeInRange(200, 299);
        var createdBody = await postResp.TextAsync();
        createdBody.Should().Contain("test-host.example");

        using var doc = JsonDocument.Parse(createdBody);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        id.Should().BeGreaterThan(0);

        // GET surface contains the new row.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/remotepathmapping");
        listResp.Status.Should().Be(200);
        (await listResp.TextAsync()).Should().Contain("test-host.example");

        // DELETE removes it.
        var delResp = await Page.APIRequest.DeleteAsync($"{RootUri}/api/v5/remotepathmapping/{id}");
        delResp.Status.Should().BeInRange(200, 299);

        // Post-DELETE surface no longer contains the host.
        var afterResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/remotepathmapping");
        (await afterResp.TextAsync()).Should().NotContain("test-host.example");
    }
}
