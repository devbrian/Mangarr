using System;
using System.Linq;
using System.Text.Json.Nodes;
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

    /// <summary>
    /// Disables the seeded Comix indexer via the indexer controller's real PUT
    /// endpoint (genuinely API-driven — unlike History/Blocklist/Queue which have
    /// no create-API). Plan 19-02's Cat B InteractiveSearch fixtures call this from
    /// their OneTimeSetUp BEFORE the first InteractiveSearch: an InteractiveSearch
    /// fans out to every enabled indexer, and ComixIndexer's PuppeteerSharp runtime
    /// signer never goes through ManagedHttpDispatcher, so CassetteHandler cannot
    /// replay it — the request would either hit live network or fail in CI Replay
    /// mode (RESEARCH Pitfall 3).
    ///
    /// Mechanism (RESEARCH §"Pattern: disabling the Comix indexer"): GET the indexer
    /// list, find the entry whose implementation == "ComixIndexer", flip all three
    /// enable flags (EnableRss / EnableAutomaticSearch / EnableInteractiveSearch) to
    /// false on the full JSON object, and PUT it back to indexer/{id}. The full
    /// object is round-tripped verbatim (parse → mutate → reserialize) so Fields /
    /// ConfigContract / Tags survive. `skipTesting=true` is passed defensively —
    /// flipping every flag false already makes ProviderDefinition.Enable false
    /// (no Test() call per ProviderControllerBase.UpdateProvider), but the query
    /// param guarantees no live Comix probe even if the seed shape changes.
    /// </summary>
    public async Task DisableComixIndexerAsync()
    {
        // 1. GET the indexer list.
        var listReq = BuildRequest("indexer", Method.GET);
        var listResponse = await _client.ExecuteAsync(listReq);
        if (!listResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.DisableComixIndexerAsync: indexer GET failed [{(int)listResponse.StatusCode}] body={listResponse.Content}");
        }

        // 2. Find the ComixIndexer entry by its implementation field.
        var indexers = JsonNode.Parse(listResponse.Content).AsArray();
        var comix = indexers.FirstOrDefault(
            n => n != null
                 && string.Equals(n["implementation"]?.GetValue<string>(), "ComixIndexer", StringComparison.Ordinal));

        if (comix == null)
        {
            throw new InvalidOperationException(
                "TestKit.DisableComixIndexerAsync: no ComixIndexer found in /api/v5/indexer — fresh-DB seed regressed?");
        }

        var comixId = (int)comix["id"]!;

        // 3. Flip every enable flag off on the full object, then PUT it back verbatim.
        comix["enableRss"] = false;
        comix["enableAutomaticSearch"] = false;
        comix["enableInteractiveSearch"] = false;

        var putReq = BuildRequest($"indexer/{comixId}?skipTesting=true", Method.PUT);

        // RestSharp 106: AddJsonBody(string) would re-serialize the string as a
        // JSON string literal. Send the already-serialized object as a raw
        // request body with the application/json content type instead.
        putReq.AddParameter("application/json", comix.ToJsonString(), ParameterType.RequestBody);
        var putResponse = await _client.ExecuteAsync(putReq);
        if (!putResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.DisableComixIndexerAsync: indexer/{comixId} PUT failed [{(int)putResponse.StatusCode}] body={putResponse.Content}");
        }
    }

    private RestRequest BuildRequest(string resource, Method method)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("X-Api-Key", _apiKey); // copied verbatim from NzbDroneRunner.cs:76
        return request;
    }
}
