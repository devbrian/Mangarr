using System;
using System.Linq;
using System.Text.Json;
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

        // Plan 19-02 (Rule 3 — auto-fix blocking issue): NzbDroneRunner.Start's
        // readiness probe polls `system/status` and treats RestSharp
        // ResponseStatus.Completed as "started" — but that is the TRANSPORT status,
        // not the HTTP status. There are TWO distinct startup-race windows:
        //   (a) the host's Kestrel listener flaps briefly right after the probe
        //       succeeds — a follow-up request gets a transport failure
        //       (ResponseStatus != Completed, StatusCode == 0); and
        //   (b) the host answers (transport-complete) before
        //       ApiKeyAuthenticationHandler / UiAuthorizationHandler finish wiring
        //       config.ApiKey, so the first authenticated request 401s.
        // Both are transient and clear within a few hundred ms. A bounded retry on
        // EITHER condition self-heals without masking a genuine misconfiguration:
        // a real bad-key 401 (or a hard-down host) persists across every attempt
        // and still throws after the budget is spent.

        // 1. Root folder
        await ExecuteWithStartupRetryAsync(
            "rootfolder POST",
            () =>
            {
                var rfRequest = BuildRequest("rootfolder", Method.POST);
                rfRequest.AddJsonBody(new { path = _tempFolderRoot });
                return rfRequest;
            });

        // 2. InProcess download client enabled. Priority defaults to 0; the
        // download-client validator requires 1-50 (FluentValidation
        // InclusiveBetweenValidator), so set it explicitly to 1. Surfaced
        // when the Plan 18-review BL-03 hardening flipped silent-swallow
        // failures into thrown exceptions.
        await ExecuteWithStartupRetryAsync(
            "downloadclient POST",
            () =>
            {
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
                return dlRequest;
            });

        // 3. Default TranslationProfile is already seeded by Phase 5 baseline migration; no-op.
    }

    // Plan 19-02 (Rule 3): execute a seed request, retrying on the transient
    // startup-race conditions described in SeedBaselineAsync — a transport
    // failure (ResponseStatus != Completed) OR a 401. Both clear within a few
    // hundred ms. A genuine bad-key 401 or a hard-down host persists across
    // every attempt and still throws after the budget is spent, so this never
    // masks a real misconfiguration. Any other 4xx/5xx (a real validation /
    // server error) throws immediately with the canonical diagnostic.
    private Task ExecuteWithStartupRetryAsync(string label, Func<RestRequest> requestFactory)
        => ExecuteWithStartupRetryAsync("SeedBaselineAsync", label, requestFactory);

    private async Task<IRestResponse> ExecuteWithStartupRetryAsync(
        string callerLabel, string label, Func<RestRequest> requestFactory)
    {
        const int maxAttempts = 12;
        IRestResponse response = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response = await _client.ExecuteAsync(requestFactory());
            if (response.IsSuccessful)
            {
                return response;
            }

            var retryable =
                response.ResponseStatus != ResponseStatus.Completed ||
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized;

            if (!retryable || attempt == maxAttempts)
            {
                break;
            }

            await Task.Delay(300);
        }

        throw new InvalidOperationException(
            $"TestKit.{callerLabel}: {label} failed [{(int)response.StatusCode}] body={response.Content}");
    }

    /// <summary>
    /// Disable the Comix indexer via the indexer PUT endpoint. Phase 19 D-05 / Plan
    /// 19-01: Comix cannot be HTTP-cassette'd (Phase 18 D-11 — the runtime signer
    /// hits comix.to live), so any Cat B / chained-grab fixture that fans out an
    /// InteractiveSearch must disable Comix in OneTimeSetUp BEFORE the first search,
    /// or the un-cassetted Comix request escapes to the live network (and throws on
    /// cassette-miss in Replay mode — RESEARCH Pitfall 3).
    ///
    /// API-driven: GET /api/v5/indexer → find Implementation == "ComixIndexer" → PUT
    /// it back with all three Enable* flags cleared (IndexerDefinition.Enable is
    /// EnableRss || EnableAutomaticSearch || EnableInteractiveSearch — clearing all
    /// three disables the indexer entirely). Mirrors SeedBaselineAsync's BuildRequest
    /// + IsSuccessful-throw pattern on BOTH the GET and the PUT.
    ///
    /// NOTE (Plan 19-02 cross-plan dependency): Plan 19-01 is the canonical home for
    /// this helper. It runs in the same Wave 1 as Plan 19-02 in a parallel worktree,
    /// so at 19-02 execution time TestKit.cs does not yet carry this method. Plan
    /// 19-02 adds it here (Rule 3 — missing dependency) matching 19-01's documented
    /// contract verbatim so the orchestrator's wave merge is a no-op identity.
    /// </summary>
    public async Task DisableComixIndexerAsync()
    {
        // 1. List indexers. Shares SeedBaselineAsync's startup-race retry — this
        // runs in a derived [OneTimeSetUp] right after the base seed, still inside
        // the host's settling window, so the same transport-flap / auth-wiring
        // race applies.
        var listResponse = await ExecuteWithStartupRetryAsync(
            "DisableComixIndexerAsync",
            "indexer GET",
            () => BuildRequest("indexer", Method.GET));

        // 2. Find the ComixIndexer entry by its Implementation field.
        using var doc = JsonDocument.Parse(listResponse.Content ?? "[]");
        var comix = doc.RootElement.EnumerateArray().FirstOrDefault(e =>
            e.TryGetProperty("implementation", out var impl) &&
            string.Equals(impl.GetString(), "ComixIndexer", StringComparison.OrdinalIgnoreCase));

        if (comix.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                "TestKit.DisableComixIndexerAsync: no ComixIndexer found in /api/v5/indexer — fresh-DB seed regressed?");
        }

        var comixId = comix.GetProperty("id").GetInt32();

        // 3. PUT the resource back with all three Enable* flags cleared. The full
        // resource JSON is round-tripped verbatim (including Fields) with only the
        // three Enable* booleans rewritten — the PUT endpoint requires the complete
        // resource shape, so we serialize the original element and override.
        var resourceText = comix.GetRawText();
        using var resourceDoc = JsonDocument.Parse(resourceText);
        var rewritten = RewriteEnableFlags(resourceDoc.RootElement);

        // RestSharp 106: AddJsonBody(string) double-encodes the string through the
        // serializer. Send the already-serialized JSON verbatim as a RequestBody
        // parameter with an application/json content type instead. Shares the
        // startup-race retry for the same reason as the GET above.
        await ExecuteWithStartupRetryAsync(
            "DisableComixIndexerAsync",
            "indexer PUT",
            () =>
            {
                var putReq = BuildRequest($"indexer/{comixId}?skipTesting=true", Method.PUT);
                putReq.AddParameter("application/json", rewritten, ParameterType.RequestBody);
                return putReq;
            });
    }

    // Serialize a JsonElement back to a raw JSON string with the three indexer
    // Enable* flags forced to false. The result is sent verbatim as a RequestBody
    // parameter (see DisableComixIndexerAsync) — no re-serialization.
    private static string RewriteEnableFlags(JsonElement original)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in original.EnumerateObject())
            {
                if (prop.NameEquals("enableRss") ||
                    prop.NameEquals("enableAutomaticSearch") ||
                    prop.NameEquals("enableInteractiveSearch"))
                {
                    writer.WriteBoolean(prop.Name, false);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private RestRequest BuildRequest(string resource, Method method)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("X-Api-Key", _apiKey); // copied verbatim from NzbDroneRunner.cs:76
        return request;
    }
}
