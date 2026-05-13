using System.Threading.Tasks;
using RestSharp;

namespace NzbDrone.Automation.Test.TestKit;

/// <summary>
/// API-level pre-seed helper per D-07. Seeds known-good defaults via /api/v5
/// before the browser opens, so fixtures don't pay the Settings UI walkthrough N times.
/// Mirrors NzbDroneRunner.cs:74-76 RestSharp + X-Api-Key pattern.
/// </summary>
public class TestKit
{
    private readonly RestClient _client;
    private readonly string _apiKey;
    private readonly string _tempFolderRoot;

    public TestKit(string rootUri, string apiKey, string tempFolderRoot)
    {
        _client = new RestClient($"{rootUri}/api/v5");
        _apiKey = apiKey;
        _tempFolderRoot = tempFolderRoot;
    }

    public async Task SeedBaselineAsync()
    {
        // 1. Root folder
        var rfRequest = BuildRequest("rootfolder", Method.POST);
        rfRequest.AddJsonBody(new { path = _tempFolderRoot });
        await _client.ExecuteAsync(rfRequest);

        // 2. InProcess download client enabled
        var dlRequest = BuildRequest("downloadclient", Method.POST);
        dlRequest.AddJsonBody(new
        {
            enable = true,
            implementation = "InProcessImageDownloadClient",
            configContract = "InProcessImageDownloadClientSettings",
            name = "InProcess (test seed)",
            fields = new object[] { }
        });
        await _client.ExecuteAsync(dlRequest);

        // 3. Default TranslationProfile is already seeded by Phase 5 baseline migration; no-op.
    }

    private RestRequest BuildRequest(string resource, Method method)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("X-Api-Key", _apiKey); // copied verbatim from NzbDroneRunner.cs:76
        return request;
    }
}
