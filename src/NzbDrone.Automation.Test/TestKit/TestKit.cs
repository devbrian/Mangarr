using System;
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
        // BL-03 (18-REVIEW): every ExecuteAsync call must check IsSuccessful and throw
        // on 4xx/5xx. Silent seed failure -> downstream AddMangaFlow.AddByMangaDexIdAsync
        // times out at 30s with no diagnostic (exactly the symptom Plan 18-14 D-D /
        // issue #102 was chasing). The seed contract is "baseline must exist before
        // browser opens"; without status-checking, that contract is unenforced.

        // 1. Root folder
        var rfRequest = BuildRequest("rootfolder", Method.POST);
        rfRequest.AddJsonBody(new { path = _tempFolderRoot });
        var rfResponse = await _client.ExecuteAsync(rfRequest);
        if (!rfResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedBaselineAsync: rootfolder POST failed [{(int)rfResponse.StatusCode}] body={rfResponse.Content}");
        }

        // 2. InProcess download client enabled. Priority defaults to 0; the
        // download-client validator requires 1-50 (FluentValidation
        // InclusiveBetweenValidator), so set it explicitly to 1. Surfaced
        // when the Plan 18-review BL-03 hardening flipped silent-swallow
        // failures into thrown exceptions.
        var dlRequest = BuildRequest("downloadclient", Method.POST);
        dlRequest.AddJsonBody(new
        {
            enable = true,
            implementation = "InProcessImageDownloadClient",
            configContract = "InProcessImageDownloadClientSettings",
            name = "InProcess (test seed)",
            priority = 1,
            fields = new object[] { }
        });
        var dlResponse = await _client.ExecuteAsync(dlRequest);
        if (!dlResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedBaselineAsync: downloadclient POST failed [{(int)dlResponse.StatusCode}] body={dlResponse.Content}");
        }

        // 3. Default TranslationProfile is already seeded by Phase 5 baseline migration; no-op.
    }

    private RestRequest BuildRequest(string resource, Method method)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("X-Api-Key", _apiKey); // copied verbatim from NzbDroneRunner.cs:76
        return request;
    }
}
