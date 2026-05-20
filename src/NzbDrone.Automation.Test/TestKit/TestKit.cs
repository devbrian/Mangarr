using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Npgsql;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
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
    private readonly PostgresOptions _postgresOptions;

    public TestKit(string rootUri, string apiKey, string tempFolderRoot, PostgresOptions postgresOptions = null)
    {
        _client = new RestClient($"{rootUri}/api/v5");
        _apiKey = apiKey;
        _tempFolderRoot = tempFolderRoot;

        // gh-152 Class 6: when the harness is configured for postgres (PR #150
        // populates NzbDroneRunner.PostgresOptions), the backend writes to the
        // per-run-uid postgres DB and no SQLite file ever exists under AppData.
        // OpenDatabase branches on this; null = sqlite mode (existing behavior).
        _postgresOptions = postgresOptions;
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

        // BL-02 (20-REVIEW): include ErrorMessage / ErrorException / ResponseStatus so
        // transport failures (StatusCode == 0, ResponseStatus != Completed) surface the
        // real cause (DNS, connection refused, task canceled) instead of "failed [0]
        // body=" with no hint — exactly the diagnostic regression Plan 18-14 chased.
        throw new InvalidOperationException(
            $"TestKit.{callerLabel}: {label} failed [{(int)response.StatusCode}] " +
            $"status={response.ResponseStatus} body={response.Content} " +
            $"error={response.ErrorMessage} exception={response.ErrorException?.Message}");
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

    // ────────────────────────────────────────────────────────────────────────
    // Phase 20 Plan 20-01 — API-driven Sonarr-canonical seeders (per D-06 / D-07).
    // Each ProviderControllerBase-descendant POST carries `?skipTesting=true`
    // (Pitfall 1 — avoids live MangaDex/Komga hit during seed). Every seeder
    // calls ExecuteWithStartupRetryAsync (Pitfall 7 — 12-attempt 401/transport
    // retry envelope). implementation/configContract strings are the canonical
    // Mangarr shapes (Pattern κ — no Sonarr TV-shape carry-overs).
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a MangaDex Indexer via
    /// <c>POST /api/v5/indexer?skipTesting=true</c>. Canonical implementation
    /// <c>"MangaDexIndexer"</c> + configContract <c>"MangaDexIndexerSettings"</c>
    /// (verified at src/NzbDrone.Core/Indexers/MangaDex/MangaDexIndexer.cs +
    /// MangaDexIndexerSettings.cs). Default rate 1.5s per MangaDex ToS.
    /// </summary>
    public async Task<int> SeedIndexerAsync(string name = "MangaDex (test seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedIndexerAsync),
            "indexer POST",
            () =>
            {
                var req = BuildRequest("indexer?skipTesting=true", Method.POST);
                req.AddJsonBody(new
                {
                    enable = true,
                    enableRss = true,
                    enableAutomaticSearch = true,
                    enableInteractiveSearch = true,
                    name,
                    implementation = "MangaDexIndexer",
                    configContract = "MangaDexIndexerSettings",
                    priority = 25,
                    fields = new object[]
                    {
                        new { name = "baseUrl", value = "https://api.mangadex.org" },
                        new { name = "sourceKey", value = "mangadex" },
                        new { name = "rateSeconds", value = 1.5 }
                    }
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds an additional InProcess DownloadClient row
    /// via <c>POST /api/v5/downloadclient?skipTesting=true</c>. SeedBaselineAsync
    /// already seeds the baseline InProcess client; this helper is for fixtures that
    /// need a second client (e.g., CRUD-test the EditDownloadClientModal). Canonical
    /// implementation <c>"InProcessImageDownloadClient"</c> + configContract
    /// <c>"InProcessImageDownloadClientSettings"</c> (matches SeedBaselineAsync L93-94).
    /// </summary>
    public async Task<int> SeedDownloadClientAsync(string name = "InProcess (Plan 20-01 seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedDownloadClientAsync),
            "downloadclient POST",
            () =>
            {
                var req = BuildRequest("downloadclient?skipTesting=true", Method.POST);
                req.AddJsonBody(new
                {
                    enable = true,
                    implementation = "InProcessImageDownloadClient",
                    configContract = "InProcessImageDownloadClientSettings",
                    name,
                    priority = 1,
                    fields = new object[] { }
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a Komga Notification via
    /// <c>POST /api/v5/connection?skipTesting=true</c> (the V5 surface is
    /// `connection` — see ConnectionController; the legacy "notification"
    /// resource was retired in Phase 15 Plan 15-10 — gh #187). Canonical implementation
    /// <c>"KomgaNotification"</c> + configContract <c>"KomgaNotificationSettings"</c>
    /// (verified at src/NzbDrone.Core/Notifications/Komga/KomgaNotification.cs +
    /// KomgaNotificationSettings.cs). Pitfall 2: LibraryId is REQUIRED — pre-populated
    /// with a placeholder int; Wave-2 fixtures override per-test if they need a
    /// specific id.
    /// </summary>
    public async Task<int> SeedNotificationAsync(string name = "Komga (test seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedNotificationAsync),
            "notification POST",
            () =>
            {
                var req = BuildRequest("connection?skipTesting=true", Method.POST);
                req.AddJsonBody(new
                {
                    onGrab = false,
                    onDownload = false,
                    onUpgrade = false,
                    onChapterImport = true,
                    onRename = false,
                    onMangaAdd = false,
                    onMangaDelete = false,
                    onHealthIssue = false,
                    includeHealthWarnings = false,
                    name,
                    implementation = "KomgaNotification",
                    configContract = "KomgaNotificationSettings",
                    fields = new object[]
                    {
                        new { name = "url", value = "http://komga.local:25600" },
                        new { name = "apiKey", value = "testkit-placeholder-key" },
                        new { name = "libraryId", value = 1 }
                    }
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a Tag via <c>POST /api/v5/tag</c>.
    /// Tag is NOT a ProviderControllerBase descendant — direct CRUD entity
    /// (label-only body; id server-assigned). No skipTesting query needed.
    /// </summary>
    public async Task<int> SeedTagAsync(string label)
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedTagAsync),
            "tag POST",
            () =>
            {
                var req = BuildRequest("tag", Method.POST);
                req.AddJsonBody(new { label });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a TranslationProfile via
    /// <c>POST /api/v5/translationprofile</c>. Mirrors the Phase-5 baseline-seeded
    /// "English Only" profile shape (Languages = ["en"]) — Wave-2 fixtures that need
    /// multi-language coverage call this with their own languages array.
    /// </summary>
    public async Task<int> SeedTranslationProfileAsync(string name = "English (test seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedTranslationProfileAsync),
            "translationprofile POST",
            () =>
            {
                var req = BuildRequest("translationprofile", Method.POST);
                req.AddJsonBody(new
                {
                    name,
                    languages = new[] { "en" },
                    allowLanguagesNotInProfile = false,
                    upgradeAllowed = true
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a CustomFormatProfile via
    /// <c>POST /api/v5/customformatprofile</c>. Empty formatItems + minFormatScore=0
    /// + maxFormatScore=null = "no score window" (NOT below-cutoff by default).
    /// Phase 16.1 D-04 default: UpgradeAllowed=false (manga CF scores subjective).
    /// </summary>
    public async Task<int> SeedCustomFormatProfileAsync(string name = "Default CF Profile (test seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedCustomFormatProfileAsync),
            "customformatprofile POST",
            () =>
            {
                var req = BuildRequest("customformatprofile", Method.POST);
                req.AddJsonBody(new
                {
                    name,
                    formatItems = new object[] { },
                    minFormatScore = 0,
                    maxFormatScore = (int?)null,
                    upgradeAllowed = false
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a CustomFormat via
    /// <c>POST /api/v5/customformat</c>.
    ///
    /// PR #173 CI-fix (2026-05-15): the CustomFormat controller validator (Mangarr.Api.V5/
    /// CustomFormats/CustomFormatController.cs:42-55) enforces BOTH
    /// `SharedValidator.RuleFor(c => c.Specifications).NotEmpty()` AND a custom rule
    /// "Must contain at least one Condition". The previous empty-specifications POST 400'd
    /// in CI (real-app boot) with both messages, cascading every CustomFormat-dependent
    /// Wave-2 fixture (List/BulkDelete/BulkEdit/Edit/Export/Manage/ManageEdit) at
    /// OneTimeSetUp. Seed a single minimal valid spec — ReleaseTitleSpecification matching
    /// the canonical `[a-z]` placeholder — so the POST clears validation. Wave-2 fixtures
    /// that need a specific spec shape can call SeedCustomFormatAsync then PUT to update.
    /// </summary>
    public async Task<int> SeedCustomFormatAsync(string name = "Test CF")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedCustomFormatAsync),
            "customformat POST",
            () =>
            {
                var req = BuildRequest("customformat", Method.POST);
                req.AddJsonBody(new
                {
                    name,
                    includeCustomFormatWhenRenaming = false,
                    specifications = new object[]
                    {
                        new
                        {
                            name = "Placeholder",
                            implementation = "ReleaseTitleSpecification",
                            implementationName = "Release Title",
                            infoLink = "https://wiki.servarr.com/sonarr/settings#custom-formats-2",
                            negate = false,
                            required = false,
                            fields = new object[]
                            {
                                new { name = "value", value = "[a-z]" }
                            }
                        }
                    }
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds a MangaDex MetadataSource via
    /// <c>POST /api/v5/metadatasource?skipTesting=true</c>. Canonical implementation
    /// <c>"MangaDexMetadataSource"</c> + configContract
    /// <c>"MangaDexMetadataSourceSettings"</c> (verified at
    /// src/NzbDrone.Core/MetadataSource/MangaDex/MangaDexMetadataSource.cs +
    /// MangaDexMetadataSourceSettings.cs). MetadataSource IS a ProviderControllerBase
    /// descendant — skipTesting=true mandatory.
    /// </summary>
    public async Task<int> SeedMetadataSourceAsync(string name = "MangaDex (test seed)")
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedMetadataSourceAsync),
            "metadatasource POST",
            () =>
            {
                var req = BuildRequest("metadatasource?skipTesting=true", Method.POST);
                req.AddJsonBody(new
                {
                    enable = true,
                    name,
                    implementation = "MangaDexMetadataSource",
                    configContract = "MangaDexMetadataSourceSettings",
                    fields = new object[]
                    {
                        new { name = "baseUrl", value = "https://api.mangadex.org" },
                        new { name = "sourceKey", value = "mangadex" }
                    }
                });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-06) — Seeds an additional RootFolder via
    /// <c>POST /api/v5/rootfolder</c>. SeedBaselineAsync already seeds the baseline
    /// root folder at <c>_tempFolderRoot</c>; this helper is for fixtures that need
    /// a SECOND root folder (e.g., the MoveManga modal between two folders).
    /// </summary>
    public async Task<int> SeedRootFolderAsync(string path)
    {
        var response = await ExecuteWithStartupRetryAsync(
            nameof(SeedRootFolderAsync),
            "rootfolder POST",
            () =>
            {
                var req = BuildRequest("rootfolder", Method.POST);
                req.AddJsonBody(new { path });
                return req;
            });

        using var doc = JsonDocument.Parse(response.Content);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Phase 26 Plan 26-06 (D-09 / D-10 bucket B) — Test fake ImportList provider
    // registration seam. Authored per RESEARCH §Q4 pattern 2 (Automation.Test
    // bootstrap hook).
    //
    // BACKGROUND: TestImportList lives in NzbDrone.Core.Test/ImportListTests/Fakes/
    // (D-09 + D-11 — test-infrastructure only). The production NzbDroneRunner-launched
    // Mangarr process auto-discovers IMangaImportList implementations via DryIoc
    // reflection scanning NzbDrone.Core assemblies only — the .Test assembly is
    // deliberately excluded from production DI (Pitfall 2 anti-prod-leak gate
    // enforced by ImportListFactoryFixture.factory_returns_zero_providers_on_empty_di_bag).
    //
    // The bucket B automation fixtures (Plan 26-06 Tasks 1-2) call this helper from
    // OneTimeSetUp to register TestImportList against the running host. The helper
    // uses the direct V5 API POST path documented in PLAN <action> Edit B:
    //   POST /api/v5/importlist
    //     { implementation = "TestImportList", configContract = "TestImportListSettings", ... }
    //
    // GRACEFUL DEGRADATION: under the current substrate the test-assembly fake is
    // NOT in the production DI scan, so the POST returns a 4xx ("Unknown
    // implementation 'TestImportList'"). The helper returns the response so the
    // fixture can branch: HTTP 2xx (id returned) → proceed with bucket B flow; HTTP
    // 4xx → Assert.Inconclusive with the documented reason and a forward-pointer to
    // Phase 27 (which lands real production providers and unblocks the bucket B
    // smoke-gate path in the host environment).
    //
    // Per the Plan 26-06 verification carve-out (worktree executor compiles only;
    // smoke gate runs live in the host): this seam compiles and is idempotent. The
    // runtime green-status of bucket B fixtures is verified by the orchestrator's
    // scripts/phase-smoke-gate.sh 26 in the main repo, NOT the worktree.

    /// <summary>
    /// Phase 26 Plan 26-06 (D-09 + D-10 bucket B) — register the test-only
    /// <c>TestImportList</c> fake against the running host via
    /// <c>POST /api/v5/importlist?skipTesting=true</c>. Idempotent: returns the
    /// existing definition id if a row with <paramref name="name"/> already exists.
    /// </summary>
    /// <param name="name">Definition display name (default: "TestImportList (test seed)").</param>
    /// <returns>
    /// <para>Tuple of (<see cref="IRestResponse"/>, <see cref="int"/>?). When the
    /// POST succeeds, the int is the newly-created (or existing) definition id.
    /// When the POST fails because <c>TestImportList</c> is not in the production
    /// DI scan, the int is null and the response carries the error body so the
    /// fixture can branch to <c>Assert.Inconclusive</c> with diagnostic context.</para>
    /// <para>The response object is returned (not thrown) so fixtures can decide
    /// between hard-fail and skip — per the Plan 26-06 bucket B documentation,
    /// the test-fake substrate-registration path is a known forward-staging gap
    /// closed by Phase 27 real providers.</para>
    /// </returns>
    public async Task<(IRestResponse Response, int? DefinitionId)> RegisterTestImportListAsync(
        string name = "TestImportList (test seed)")
    {
        // 1. Idempotency check — if a definition with this Name already exists, return its id.
        var listResponse = await _client.ExecuteAsync(BuildRequest("importlist", Method.GET));
        if (listResponse.IsSuccessful && !string.IsNullOrEmpty(listResponse.Content))
        {
            using var listDoc = JsonDocument.Parse(listResponse.Content);
            foreach (var element in listDoc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("name", out var nameProp) &&
                    string.Equals(nameProp.GetString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return (listResponse, element.GetProperty("id").GetInt32());
                }
            }
        }

        // 2. POST — register TestImportList via V5. skipTesting=true mandatory per
        //    ProviderControllerBase pattern (Pitfall 1 — the substrate's Test() path
        //    runs the fake provider's Test() override which is a no-op, but
        //    skipTesting still short-circuits the validation cascade defensively).
        var postRequest = BuildRequest("importlist?skipTesting=true", Method.POST);
        postRequest.AddJsonBody(new
        {
            enable = true,
            enableAutomaticAdd = true,
            searchForMissingChapters = false,
            shouldMonitor = "all",
            monitorNewItems = "all",
            rootFolderPath = _tempFolderRoot,
            translationProfileId = 1,
            customFormatProfileId = 1,
            name,
            implementation = "TestImportList",
            configContract = "TestImportListSettings",
            fields = new object[]
            {
                new { name = "baseUrl", value = "https://test.invalid" }
            },
            tags = new int[] { }
        });

        var postResponse = await _client.ExecuteAsync(postRequest);
        if (!postResponse.IsSuccessful)
        {
            // Forward-stage failure mode — TestImportList isn't in production DI scan
            // until Phase 27 lands a substrate-extensibility seam OR a real provider.
            // Return non-throwing so the fixture can decide (Inconclusive vs hard-fail).
            return (postResponse, null);
        }

        using var doc = JsonDocument.Parse(postResponse.Content);
        return (postResponse, doc.RootElement.GetProperty("id").GetInt32());
    }

    /// <summary>
    /// Phase 27 — register the production <c>MangaDexImportList</c> provider against
    /// the running host via <c>POST /api/v5/importlist?skipTesting=true</c>. Mirrors
    /// <see cref="RegisterTestImportListAsync"/> shape but targets the real Phase 27
    /// provider so bucket-B automation fixtures (CRUD round-trip, sync trigger,
    /// auto-exclusion on delete, cross-vertical exclusion) can execute end-to-end
    /// instead of branching to <c>Assert.Inconclusive</c>. Closes GH #217.
    ///
    /// Dummy credentials are used (the round-trip / CRUD / sync-trigger / delete-
    /// exclusion flows do NOT require working OAuth — they exercise the V5 controller
    /// surface, the substrate sync orchestrator, and the MangaService delete path).
    /// </summary>
    /// <param name="name">Definition display name (default: "MangaDex (test seed)").</param>
    /// <returns>Tuple of (response, definition id) once the row is registered.</returns>
    public async Task<(IRestResponse Response, int? DefinitionId)> RegisterMangaDexImportListAsync(
        string name = "MangaDex (test seed)")
    {
        // Both V5 calls below go through ExecuteWithStartupRetryAsync to absorb the
        // transient transport / 401-startup-race conditions that the SeedBaselineAsync
        // helper handles. Without the retry envelope, the listing GET or POST can
        // intermittently fail when the host is mid-boot (per WR-08 / Phase 18 D-14).
        //
        // ExecuteWithStartupRetryAsync throws InvalidOperationException on hard
        // failure (max-retries exhausted on transient errors OR non-retryable status
        // like 4xx with a real error body). We intentionally do NOT catch and collapse
        // that into (null, null) — fixtures need the full diagnostic context (status
        // code + response body + ResponseStatus + error message) embedded in the
        // exception so the test failure clearly identifies the root cause. Letting
        // the exception bubble matches the discipline used by other TestKit seeders
        // (SeedBaselineAsync + sibling Seed*Async helpers all throw on hard failure).
        const string Caller = nameof(RegisterMangaDexImportListAsync);

        // 1. Idempotency check — if a definition with this Name already exists, return its id.
        var listResponse = await ExecuteWithStartupRetryAsync(
            Caller,
            "importlist GET",
            () => BuildRequest("importlist", Method.GET));

        if (!string.IsNullOrEmpty(listResponse.Content))
        {
            using var listDoc = JsonDocument.Parse(listResponse.Content);
            foreach (var element in listDoc.RootElement.EnumerateArray())
            {
                // Match on BOTH Name + Implementation — without the implementation guard
                // we'd risk returning a non-MangaDex row whose user-supplied Name happened
                // to collide with our test seed name (e.g., an AniList row called
                // "MangaDex (test seed)" from a previous test in the same DB).
                if (element.TryGetProperty("name", out var nameProp) &&
                    element.TryGetProperty("implementation", out var implProp) &&
                    string.Equals(nameProp.GetString(), name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(implProp.GetString(), "MangaDexImportList", StringComparison.OrdinalIgnoreCase))
                {
                    return (listResponse, element.GetProperty("id").GetInt32());
                }
            }
        }

        // 2. POST — register MangaDexImportList via V5. skipTesting=true bypasses the
        //    Settings.Validate cascade which would otherwise reject dummy ClientId/
        //    ClientSecret/Username/Password as "Required" — we just need the row in
        //    the DB so the CRUD / sync / delete-exclusion flows can fire. Same
        //    fail-loud discipline as the listing call above — InvalidOperationException
        //    bubbles with full diagnostics.
        var postResponse = await ExecuteWithStartupRetryAsync(
            Caller,
            "importlist POST",
            () =>
            {
                var postRequest = BuildRequest("importlist?skipTesting=true", Method.POST);
                postRequest.AddJsonBody(new
                {
                    enable = true,
                    enableAutomaticAdd = false,
                    searchForMissingChapters = false,
                    shouldMonitor = "all",
                    monitorNewItems = "all",
                    rootFolderPath = _tempFolderRoot,
                    translationProfileId = 1,
                    customFormatProfileId = 1,
                    name,
                    implementation = "MangaDexImportList",
                    configContract = "MangaDexImportListSettings",
                    fields = new object[]
                    {
                        new { name = "clientId", value = "dummy-client-id" },
                        new { name = "clientSecret", value = "dummy-client-secret" },
                        new { name = "username", value = "dummy-user" },
                        new { name = "password", value = "dummy-password" }
                    },
                    tags = new int[] { }
                });
                return postRequest;
            });

        using var doc = JsonDocument.Parse(postResponse.Content);
        return (postResponse, doc.RootElement.GetProperty("id").GetInt32());
    }

    /// <summary>
    /// Phase 27 — register the production <c>MalImportList</c> provider against
    /// the running host via <c>POST /api/v5/importlist?skipTesting=true</c>. Mirrors
    /// <see cref="RegisterMangaDexImportListAsync"/> but targets the MAL provider so
    /// fixtures that need a real Definition.Id (e.g. RequestActionRoundTripFixture's
    /// MAL test — MAL's startOAuth requires a persisted definition to round-trip the
    /// PKCE state per Phase 27 D-09 fail-fast guard) can register a row first.
    ///
    /// Dummy client_id is used — MAL public-client PKCE has no client_secret, so the
    /// URL-construction path (BuildAuthorizeUrl) only needs ClientId populated to
    /// emit the canonical authorize URL.
    /// </summary>
    public async Task<(IRestResponse Response, int? DefinitionId)> RegisterMalImportListAsync(
        string name = "MyAnimeList (test seed)")
    {
        const string Caller = nameof(RegisterMalImportListAsync);

        // 1. Idempotency check — match on Name + Implementation.
        var listResponse = await ExecuteWithStartupRetryAsync(
            Caller,
            "importlist GET",
            () => BuildRequest("importlist", Method.GET));

        if (!string.IsNullOrEmpty(listResponse.Content))
        {
            using var listDoc = JsonDocument.Parse(listResponse.Content);
            foreach (var element in listDoc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("name", out var nameProp) &&
                    element.TryGetProperty("implementation", out var implProp) &&
                    string.Equals(nameProp.GetString(), name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(implProp.GetString(), "MalImportList", StringComparison.OrdinalIgnoreCase))
                {
                    return (listResponse, element.GetProperty("id").GetInt32());
                }
            }
        }

        // 2. POST — register MalImportList via V5. skipTesting=true bypasses the
        //    Settings.Validate cascade. Dummy ClientId is fine; the round-trip test
        //    asserts URL-shape only, not OAuth success.
        var postResponse = await ExecuteWithStartupRetryAsync(
            Caller,
            "importlist POST",
            () =>
            {
                var postRequest = BuildRequest("importlist?skipTesting=true", Method.POST);
                postRequest.AddJsonBody(new
                {
                    enable = true,
                    enableAutomaticAdd = false,
                    searchForMissingChapters = false,
                    shouldMonitor = "all",
                    monitorNewItems = "all",
                    rootFolderPath = _tempFolderRoot,
                    translationProfileId = 1,
                    customFormatProfileId = 1,
                    name,
                    implementation = "MalImportList",
                    configContract = "MalImportListSettings",
                    fields = new object[]
                    {
                        new { name = "clientId", value = "fixture-mal-client-id" },
                        new { name = "status", value = 0 }
                    },
                    tags = new int[] { }
                });
                return postRequest;
            });

        using var doc = JsonDocument.Parse(postResponse.Content);
        return (postResponse, doc.RootElement.GetProperty("id").GetInt32());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Raw-SQLite failure-state / queue seed helpers (Plan 19-01 Open Question 1
    // verdict = raw-SQLite). The automation harness runs Mangarr as a SEPARATE
    // OS process with no shared DI container, so it cannot call
    // IChapterHistoryRepository / IMangaBlocklistRepository /
    // IMangaPendingReleaseRepository. The only honest mechanism for a
    // *failed*-download history row, a blocklist row, or a pending-queue row is
    // a direct SQLite write into the backend's per-fixture <AppData>/mangarr.db.
    //
    // The Open Question 1 spike proved the round-trip is clean: a ChapterHistory
    // DownloadFailed row inserted directly into mangarr.db came back through
    // GET /api/v5/manga/history with HTTP 200 and every field intact.
    //
    // D-04 (SC#8 gate): each seeded row mirrors the canonical service-layer
    // builder by construction —
    //   ChapterHistoryService.Handle(ChapterDownloadFailedEvent)
    //   MangaBlocklistService.Handle(ChapterDownloadFailedEvent)
    //   MangaPendingReleaseService.Insert (the Add path)
    // — and the JSON columns (Data / ChapterIds / ParsedChapterInfo / Release)
    // are serialized with the SAME System.Text.Json options the backend's
    // EmbeddedDocumentConverter<T> uses (camelCase, indented, enum-as-string,
    // ignore-null), so Dapper's read-back deserializer cannot trip a fidelity
    // bug. T-19-01: the DB path is derived from the harness-owned AppData dir
    // passed by the caller (Runner.AppData) — never a test-controlled string
    // that could escape the per-fixture sandbox.

    /// <summary>
    /// The EmbeddedDocumentConverter&lt;T&gt; serializer settings, copied verbatim
    /// from src/NzbDrone.Core/Datastore/Converters/EmbeddedDocumentConverter.cs.
    /// Any JSON column a seed helper writes MUST use these exact options or the
    /// backend's Dapper read-back will not round-trip.
    /// </summary>
    private static readonly JsonSerializerOptions EmbeddedDocumentSettings = BuildEmbeddedDocumentSettings();

    private static JsonSerializerOptions BuildEmbeddedDocumentSettings()
    {
        var settings = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        settings.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true));

        return settings;
    }

    /// <summary>
    /// Seeds one <c>ChapterHistory</c> row with <c>EventType = DownloadFailed</c>
    /// (=2) and <c>Successful = 0</c> into the backend's <c>mangarr.db</c>,
    /// mirroring <c>ChapterHistoryService.Handle(ChapterDownloadFailedEvent)</c>
    /// exactly — including the <c>Data</c> dictionary key set
    /// (<c>DownloadClient, Message, Source, Indexer</c>) per the RESEARCH §Q-4
    /// schema lock. Cat A's <c>HistoryRetryFixture</c> calls this so its retry
    /// path has a real failed row to act on.
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="chapterId">FK to a chapter of that manga.</param>
    public Task SeedHistoryFailedAsync(string appDataPath, int mangaId, int chapterId)
    {
        // Mirror ChapterHistoryService.Handle(ChapterDownloadFailedEvent): the
        // Data dictionary carries exactly these four keys.
        var data = new Dictionary<string, string>
        {
            { "DownloadClient", "InProcessImageDownloadClient" },
            { "Message", "TestKit-seeded failed download" },
            { "Source", string.Empty },
            { "Indexer", "MangaDex" }
        };

        using var connection = OpenDatabase(appDataPath);
        using var command = connection.CreateCommand();

        // Identifiers double-quoted so postgres preserves case (unquoted postgres
        // folds `ChapterHistory` -> `chapterhistory` and the relation isn't found).
        // SQLite accepts double-quoted identifiers per ANSI SQL. Mirrors the
        // canonical pattern at `BasicRepository.cs:125` (`FROM "{state.table}"`).
        command.CommandText =
            "INSERT INTO \"ChapterHistory\" " +
            "(\"MangaId\", \"ChapterId\", \"EventType\", \"Date\", \"SourceTitle\", \"DownloadId\", " +
            " \"TranslatedLanguage\", \"ScanlationGroup\", \"SourceKey\", \"ReleaseGuid\", \"Data\", \"Successful\") " +
            "VALUES " +
            "(@MangaId, @ChapterId, @EventType, @Date, @SourceTitle, @DownloadId, " +
            " @TranslatedLanguage, @ScanlationGroup, @SourceKey, @ReleaseGuid, @Data, @Successful)";
        AddParam(command, "@MangaId", mangaId);
        AddParam(command, "@ChapterId", chapterId);
        AddParam(command, "@EventType", 2); // ChapterHistoryEventType.DownloadFailed
        AddParam(command, "@Date", DateTime.UtcNow);
        AddParam(command, "@SourceTitle", "TestKit Seeded Release - Chapter");
        AddParam(command, "@DownloadId", "testkit-failed-download-id");
        AddParam(command, "@TranslatedLanguage", "en");
        AddParam(command, "@ScanlationGroup", DBNull.Value);
        AddParam(command, "@SourceKey", "MangaDex");
        AddParam(command, "@ReleaseGuid", "testkit-failed-release-guid");
        AddParam(command, "@Data", JsonSerializer.Serialize(data, EmbeddedDocumentSettings));

        // Successful column is AsBoolean(): postgres has a native boolean type
        // (rejects int->bool implicit casts); SQLite stores 0/1 as INTEGER but
        // accepts bool fine via System.Data.SQLite. Pass `false` so both work.
        AddParam(command, "@Successful", false);

        var inserted = command.ExecuteNonQuery();
        if (inserted != 1)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedHistoryFailedAsync: expected 1 ChapterHistory row inserted, got {inserted}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Seeds one <c>MangaBlocklist</c> row into the backend's <c>mangarr.db</c>,
    /// mirroring <c>MangaBlocklistService.Handle(ChapterDownloadFailedEvent)</c>
    /// exactly — the D-11 release-identity triple
    /// (<c>SourceKey, ReleaseGuid, SourceTitle</c>) plus the <c>ChapterIds</c>
    /// JSON column. Cat A's <c>BlocklistBulkRemoveFixture</c> calls this so its
    /// bulk-DELETE path has a real blocklist row to remove.
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="chapterId">A chapter id of that manga — stored in the ChapterIds JSON array.</param>
    public Task SeedBlocklistAsync(string appDataPath, int mangaId, int chapterId)
    {
        // Mirror MangaBlocklistService.Handle(ChapterDownloadFailedEvent):
        // ChapterIds is a List<int> JSON column; the triple is populated from
        // the (failed) release identity.
        var chapterIds = new List<int> { chapterId };

        using var connection = OpenDatabase(appDataPath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"MangaBlocklist\" " +
            "(\"MangaId\", \"ChapterIds\", \"SourceTitle\", \"SourceKey\", \"ReleaseGuid\", " +
            " \"ReleaseInfoJson\", \"Date\", \"Reason\", \"Source\") " +
            "VALUES " +
            "(@MangaId, @ChapterIds, @SourceTitle, @SourceKey, @ReleaseGuid, " +
            " @ReleaseInfoJson, @Date, @Reason, @Source)";
        AddParam(command, "@MangaId", mangaId);
        AddParam(command, "@ChapterIds", JsonSerializer.Serialize(chapterIds, EmbeddedDocumentSettings));
        AddParam(command, "@SourceTitle", "TestKit Seeded Release - Chapter");
        AddParam(command, "@SourceKey", "MangaDex");
        AddParam(command, "@ReleaseGuid", "testkit-blocklist-release-guid");
        AddParam(command, "@ReleaseInfoJson", DBNull.Value);
        AddParam(command, "@Date", DateTime.UtcNow);
        AddParam(command, "@Reason", "TestKit-seeded blocklist entry");
        AddParam(command, "@Source", "TestKit");

        var inserted = command.ExecuteNonQuery();
        if (inserted != 1)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedBlocklistAsync: expected 1 MangaBlocklist row inserted, got {inserted}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-04 / Blocker #4) — Seeds a ChapterFile row via
    /// raw-SQLite. No public ChapterFile create-API exists (the import pipeline is
    /// the only production code path and requires real downloaded files). Wave-3
    /// fixtures (BulkRenamePreview, OrganizePreview, ChapterFileDelete,
    /// ChapterFileBulkDelete) call this to assert against real state instead of
    /// Assert.Inconclusive. Per Phase 19 D-04 precedent: parameterized SQLite +
    /// canonical column set (verified at ChapterFile.cs + 001_mangarr_baseline.cs:641-650).
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="chapterId">FK to a chapter of that manga.</param>
    /// <param name="relativePath">CBZ relative path (e.g. "Chapter 1.cbz") used by the disk-name builder.</param>
    /// <returns>The generated ChapterFile.Id.</returns>
    public Task<int> SeedChapterFileAsync(string appDataPath, int mangaId, int chapterId, string relativePath)
    {
        // Schema (001_mangarr_baseline.cs:641-650): MangaId(NN) + ChapterId(NN) +
        // RelativePath(NN) + Path(NN) + Size(NN) + DateAdded(NN) +
        // OriginalFilePath(null) + TranslatedLanguage(null) + ScanlationGroup(null).
        // No JSON columns — no EmbeddedDocumentSettings serialization needed.
        // Mirrors SeedCutoffUnmetChapterAsync ChapterFiles INSERT shape exactly.
        var fullPath = $"/testkit/chapterfile/manga-{mangaId}/{relativePath}";
        int chapterFileId;

        using (var connection = OpenDatabase(appDataPath))
        {
            using var command = connection.CreateCommand();

            // Branch on SQLite vs postgres for the id-recovery hop (last_insert_rowid
            // vs RETURNING). Mirrors SeedCutoffUnmetChapterAsync L605-625.
            if (_postgresOptions != null && _postgresOptions.Host.IsNotNullOrWhiteSpace())
            {
                command.CommandText =
                    "INSERT INTO \"ChapterFiles\" " +
                    "(\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\", " +
                    " \"OriginalFilePath\", \"TranslatedLanguage\", \"ScanlationGroup\") " +
                    "VALUES " +
                    "(@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded, " +
                    " @OriginalFilePath, @TranslatedLanguage, @ScanlationGroup) " +
                    "RETURNING \"Id\";";
            }
            else
            {
                command.CommandText =
                    "INSERT INTO \"ChapterFiles\" " +
                    "(\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\", " +
                    " \"OriginalFilePath\", \"TranslatedLanguage\", \"ScanlationGroup\") " +
                    "VALUES " +
                    "(@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded, " +
                    " @OriginalFilePath, @TranslatedLanguage, @ScanlationGroup); " +
                    "SELECT last_insert_rowid();";
            }

            AddParam(command, "@MangaId", mangaId);
            AddParam(command, "@ChapterId", chapterId);
            AddParam(command, "@RelativePath", relativePath);
            AddParam(command, "@Path", fullPath);
            AddParam(command, "@Size", 1024L);
            AddParam(command, "@DateAdded", DateTime.UtcNow);
            AddParam(command, "@OriginalFilePath", DBNull.Value);
            AddParam(command, "@TranslatedLanguage", "en");
            AddParam(command, "@ScanlationGroup", DBNull.Value);

            var newId = command.ExecuteScalar();
            if (newId == null || newId is DBNull)
            {
                throw new InvalidOperationException(
                    "TestKit.SeedChapterFileAsync: ChapterFiles INSERT returned no id");
            }

            chapterFileId = Convert.ToInt32(newId);

            // debug-30 (2026-05-16): also link the Chapter row to this file so
            // downstream `RenameChapterFileService.GetPreviews()` (which filters
            // `chapters.Where(c => c.ChapterFileId == file.Id)`) actually sees
            // the seeded pair. Without this UPDATE the rename pipeline yields
            // nothing — silently empty [] response. Mirrors SeedCutoffUnmetChapterAsync
            // L1185+ which also does the Chapter-side UPDATE step.
            using var updateChapter = connection.CreateCommand();
            updateChapter.CommandText =
                "UPDATE \"Chapters\" " +
                "SET \"ChapterFileId\" = @ChapterFileId " +
                "WHERE \"Id\" = @ChapterId;";
            AddParam(updateChapter, "@ChapterFileId", chapterFileId);
            AddParam(updateChapter, "@ChapterId", chapterId);
            var rowsUpdated = updateChapter.ExecuteNonQuery();
            if (rowsUpdated != 1)
            {
                throw new InvalidOperationException(
                    $"TestKit.SeedChapterFileAsync: expected 1 Chapters row updated for id={chapterId}, got {rowsUpdated}. " +
                    "Chapter ↔ ChapterFile link is required so RenameChapterFileService.GetPreviews() yields rows.");
            }
        }

        return Task.FromResult(chapterFileId);
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-04 / Blocker #4) — Creates an in-process-filesystem
    /// folder populated with deterministic placeholder CBZ/image files so the
    /// InteractiveImport modal can render rows without performing network IO.
    /// No public create-API exists for "InteractiveImport folder" — it's a filesystem
    /// path that the InteractiveImport scanner reads from. Per Phase 19 D-04 lineage:
    /// the harness owns the path via the supplied <paramref name="folderPath"/>.
    /// </summary>
    /// <param name="folderPath">Absolute folder path to create + populate.</param>
    /// <param name="mangaId">Optional manga id — when supplied, the folder encodes a
    /// parseable manga title via Parser regex so InteractiveImport can suggest the
    /// match. When null, the folder uses a generic deterministic title.</param>
    /// <returns>The absolute folderPath (echo of input — provided so the caller can
    /// pass it directly to the InteractiveImport modal without re-computing).</returns>
    public Task<string> SeedInteractiveImportFolderAsync(string folderPath, int? mangaId = null)
    {
        // 1. Create the folder (idempotent — CreateDirectory no-ops if it exists).
        Directory.CreateDirectory(folderPath);

        // 2. Drop 1-2 placeholder files inside (deterministic names so fixtures can
        //    assert exact entries). File names encode "Manga Title - Chapter N" so
        //    the Parser regex sees them as parseable manga releases.
        var titleSlug = mangaId.HasValue
            ? $"TestKit Manga {mangaId.Value}"
            : "TestKit Placeholder Manga";

        var cbzPath = Path.Combine(folderPath, $"{titleSlug} - Chapter 001 [en].cbz");
        if (!File.Exists(cbzPath))
        {
            // Minimal valid CBZ — empty ZIP signature so the file is recognized as
            // a CBZ archive without bringing image decoders into the harness.
            File.WriteAllBytes(cbzPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        var imgPath = Path.Combine(folderPath, $"{titleSlug} - Chapter 002 [en].cbz");
        if (!File.Exists(imgPath))
        {
            File.WriteAllBytes(imgPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        }

        return Task.FromResult(folderPath);
    }

    /// <summary>
    /// Phase 20 Plan 20-01 (D-04 / Blocker #4) — Seeds a MangaPendingReleases row
    /// (the live queue table — D-03 queue seam per SeedPendingQueueItemAsync) and
    /// returns a deterministic download identifier string. Activity/Queue fixtures
    /// (Plan 20-09) call this so their <c>DELETE /api/v5/manga/queue/{id}</c> + bulk
    /// remove paths have a real queue row to act on. Per Phase 19 D-04 — no public
    /// "Download" create-API beyond the indexer-grab path which requires live HTTP;
    /// raw-SQLite is the only honest seed mechanism.
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="chapterId">FK to a chapter of that manga (encoded in ParsedChapterInfo).</param>
    /// <param name="status">The download status this row represents (Queued/Downloading/Completed/Failed/etc.).</param>
    /// <returns>The deterministic downloadId string the fixture can assert against.</returns>
    public async Task<string> SeedDownloadAsync(string appDataPath, int mangaId, int chapterId, DownloadItemStatus status)
    {
        // The MangaPendingReleases table is the only live queue source per
        // SeedPendingQueueItemAsync comment block (the in-memory MangaQueueService._queue
        // is structurally dead at runtime; TrackedDownloadRefreshedEvent never fires
        // in production). We mirror that helper's shape exactly with a parameterized
        // status so Wave-3 Activity fixtures can seed Downloading / Failed / Completed
        // states without dragging the live indexer-grab path into the harness.
        //
        // NOTE: MangaPendingReleases does not have a DB-level Status column — the
        // PendingReleaseReason enum (Delay/DownloadClientUnavailable/Fallback) is the
        // closest persisted analog. We map DownloadItemStatus → PendingReleaseReason
        // for the on-disk write (Queued/Paused → Delay; Downloading → Delay;
        // Failed/Warning → DownloadClientUnavailable; Completed → Fallback) so the
        // queue projection's pending-half presents a row in a sensible state.
        var reason = status switch
        {
            DownloadItemStatus.Failed => PendingReleaseReason.DownloadClientUnavailable,
            DownloadItemStatus.Warning => PendingReleaseReason.DownloadClientUnavailable,
            DownloadItemStatus.Completed => PendingReleaseReason.Fallback,
            _ => PendingReleaseReason.Delay
        };

        var downloadId = $"testkit-download-{mangaId}-{chapterId}-{Guid.NewGuid():N}";
        var releaseTitle = $"TestKit Manga {mangaId} - Chapter {chapterId} [en]";

        var parsedChapterInfo = new ParsedChapterInfo
        {
            ReleaseTitle = releaseTitle,
            MangaTitle = $"TestKit Manga {mangaId}",
            ChapterNumbers = new[] { (decimal)chapterId },
            TranslatedLanguage = "en"
        };

        var release = new ReleaseInfo
        {
            Guid = downloadId,
            Title = releaseTitle,
            Size = 1024,
            Indexer = "MangaDex",
            IndexerId = 1,
            DownloadProtocol = DownloadProtocol.Unknown,
            PublishDate = DateTime.UtcNow,
            TranslatedLanguage = "en"
        };

        using (var connection = OpenDatabase(appDataPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"MangaPendingReleases\" " +
                "(\"MangaId\", \"Title\", \"Added\", \"ParsedChapterInfo\", \"Release\", \"Reason\") " +
                "VALUES " +
                "(@MangaId, @Title, @Added, @ParsedChapterInfo, @Release, @Reason)";
            AddParam(command, "@MangaId", mangaId);
            AddParam(command, "@Title", releaseTitle);
            AddParam(command, "@Added", DateTime.UtcNow);
            AddParam(command, "@ParsedChapterInfo", JsonSerializer.Serialize(parsedChapterInfo, EmbeddedDocumentSettings));
            AddParam(command, "@Release", JsonSerializer.Serialize(release, EmbeddedDocumentSettings));
            AddParam(command, "@Reason", (int)reason);

            var inserted = command.ExecuteNonQuery();
            if (inserted != 1)
            {
                throw new InvalidOperationException(
                    $"TestKit.SeedDownloadAsync: expected 1 MangaPendingReleases row inserted, got {inserted}");
            }
        }

        // Trigger the in-memory pending-releases projection rebuild so the
        // GET /api/v5/manga/queue caller sees the new row (mirrors
        // SeedPendingQueueItemAsync). Without this, the static cache is stale.
        await TriggerPendingReleaseRebuildAsync();

        return downloadId;
    }

    /// <summary>
    /// Seeds one <c>MangaPendingReleases</c> row into the backend's
    /// <c>mangarr.db</c>, mirroring <c>MangaPendingReleaseService.Insert</c>
    /// (the <c>Add</c> path) exactly. This is the D-03 queue seam: the in-memory
    /// <c>MangaQueueService._queue</c> is structurally dead at runtime
    /// (<c>TrackedDownloadRefreshedEvent</c> is never published in production),
    /// so the pending-releases table is the only live queue source. On the next
    /// <c>GET /api/v5/manga/queue</c>, <c>MangaQueueController</c> includes this
    /// row in the projection's pending half. Cat A's
    /// <c>QueueRowRemoveFixture</c> calls this so its
    /// <c>DELETE /api/v5/manga/queue/{id}</c> path has a real pending row to
    /// remove. <c>RemoteChapter</c> is NOT persisted — it is a service-projection
    /// field rebuilt from <c>ParsedChapterInfo</c>.
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="mangaTitle">The seeded manga's title — used to build a canonical-shaped ParsedChapterInfo.</param>
    public async Task SeedPendingQueueItemAsync(string appDataPath, int mangaId, string mangaTitle)
    {
        // Mirror MangaPendingReleaseService.Insert: MangaId, ParsedChapterInfo,
        // Release, Title, Added, Reason. ParsedChapterInfo + Release are
        // EmbeddedDocumentConverter-serialized JSON columns — build the real
        // POCOs and serialize with the canonical options so the GetPendingQueue
        // projection's Dapper read-back round-trips.
        var releaseTitle = $"{mangaTitle} - Chapter 1 [en]";

        var parsedChapterInfo = new ParsedChapterInfo
        {
            ReleaseTitle = releaseTitle,
            MangaTitle = mangaTitle,
            ChapterNumbers = new[] { 1m },
            TranslatedLanguage = "en"

            // ChapterType defaults to Regular in the POCO ctor; VolumeNumber /
            // AbsoluteChapterNumber / Title / ScanlationGroup stay null and are
            // dropped by DefaultIgnoreCondition.WhenWritingNull.
        };

        var release = new ReleaseInfo
        {
            Guid = "testkit-pending-release-guid",
            Title = releaseTitle,
            Size = 1024,
            Indexer = "MangaDex",
            IndexerId = 1,
            DownloadProtocol = DownloadProtocol.Unknown,
            PublishDate = DateTime.UtcNow,
            TranslatedLanguage = "en"
        };

        using (var connection = OpenDatabase(appDataPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"MangaPendingReleases\" " +
                "(\"MangaId\", \"Title\", \"Added\", \"ParsedChapterInfo\", \"Release\", \"Reason\") " +
                "VALUES " +
                "(@MangaId, @Title, @Added, @ParsedChapterInfo, @Release, @Reason)";
            AddParam(command, "@MangaId", mangaId);
            AddParam(command, "@Title", releaseTitle);
            AddParam(command, "@Added", DateTime.UtcNow);
            AddParam(command, "@ParsedChapterInfo", JsonSerializer.Serialize(parsedChapterInfo, EmbeddedDocumentSettings));
            AddParam(command, "@Release", JsonSerializer.Serialize(release, EmbeddedDocumentSettings));
            AddParam(command, "@Reason", (int)PendingReleaseReason.Delay);

            var inserted = command.ExecuteNonQuery();
            if (inserted != 1)
            {
                throw new InvalidOperationException(
                    $"TestKit.SeedPendingQueueItemAsync: expected 1 MangaPendingReleases row inserted, got {inserted}");
            }
        }

        // Trigger a _pendingReleases projection rebuild. MangaPendingReleaseService
        // serves GET /api/v5/manga/queue from a STATIC _pendingReleases cache that
        // is only rebuilt on its 9 IHandle events — a raw INSERT after backend boot
        // leaves that cache stale, so the row would not surface in the queue
        // projection. Saving a config value publishes ConfigSavedEvent, which the
        // service handles by calling UpdatePendingReleases() (re-reads the repo,
        // so the just-inserted row is picked up). DownloadClientWorkingFolders is
        // toggled to a unique value so ConfigService.SaveConfigDictionary actually
        // fires the event (it short-circuits when no value changed). The per-fixture
        // DB is wiped by Phase 18 D-05, so the config delta never leaks.
        await TriggerPendingReleaseRebuildAsync();
    }

    /// <summary>
    /// Seeds a below-cutoff chapter for the given <paramref name="mangaId"/> + <paramref name="chapterId"/>
    /// pair, satisfying the <c>ChapterCutoffService.ChaptersWhereCutoffUnmet</c>
    /// eligibility predicate (gh #153). The cutoff-unmet feed
    /// (<c>GET /api/v5/manga/wanted/cutoff</c>) returns only chapters whose:
    /// <list type="number">
    /// <item><c>ChapterFile</c> is non-null (chapter has an imported file)</item>
    /// <item><c>Chapter.Monitored == true</c></item>
    /// <item>parent <c>Manga</c> is assigned to a <c>TranslationProfile</c> with
    /// <c>Languages.Count &gt; 1</c> <em>OR</em> a <c>CustomFormatProfile</c> with
    /// <c>MinFormatScore &gt; 0 || MaxFormatScore.HasValue</c>
    /// (per <c>ChapterCutoffService.cs:63-104</c>).</item>
    /// </list>
    /// <para>The fresh-DB baseline ships an <c>"English Only"</c> TranslationProfile
    /// (single language ⇒ NOT below cutoff) and a default CustomFormatProfile with
    /// <c>MinFormatScore=0</c> + <c>MaxFormatScore=null</c> (no score window ⇒ NOT below cutoff),
    /// so no manga is ever eligible without explicit mutation. This helper:</para>
    /// <list type="number">
    /// <item>Creates a multi-language <c>TranslationProfile</c> via <c>POST /api/v5/translationprofile</c>
    /// (Languages = ["en", "ja"] — both BCP-47-valid via <c>IsoLanguages.Find</c>).</item>
    /// <item>Re-assigns the seeded manga to the new profile via <c>PUT /api/v5/manga/{id}</c>
    /// (<c>MangaResource.ApplyChanges</c> round-trips <c>TranslationProfileId</c>).</item>
    /// <item>Inserts a <c>ChapterFile</c> row via raw-SQLite (mirroring the
    /// <c>SeedHistoryFailedAsync</c> / <c>SeedPendingQueueItemAsync</c> precedent).</item>
    /// <item>Updates the target Chapter's <c>ChapterFileId</c> FK + forces <c>Monitored = true</c>
    /// in the same UPDATE so the cutoff service's <c>c.Monitored</c> filter holds
    /// regardless of any chapter-level monitor toggle.</item>
    /// </list>
    /// <para>Cat A's <c>MangaCutoffUnmetFixture</c> calls this so the populated-row
    /// branch (per-row <c>current-quality</c> + <c>cutoff-quality</c> testids) is
    /// reachable deterministically — the populated path is the only path
    /// (the empty-state branch was deleted per gh #153 / Plan 19-05 D-03 precedent).</para>
    /// </summary>
    /// <param name="appDataPath">The backend's per-fixture data dir — pass <c>Runner.AppData</c>.</param>
    /// <param name="mangaId">FK to a manga the fixture already seeded via AddMangaFlow.</param>
    /// <param name="chapterId">FK to a chapter of that manga.</param>
    public async Task SeedCutoffUnmetChapterAsync(string appDataPath, int mangaId, int chapterId)
    {
        // 1. POST a multi-language TranslationProfile. Languages.Count > 1 is the
        //    canonical cutoff predicate (ChapterCutoffService:68-76). "en"+"ja"
        //    both pass IsoLanguages.Find and stay under the 50-entry / 32-char-each
        //    validator caps. The new profile id round-trips back so step 2 can swap
        //    the manga's TranslationProfileId in.
        var profileRequest = BuildRequest("translationprofile", Method.POST);
        profileRequest.AddJsonBody(new
        {
            name = $"TestKit Cutoff Multi-Lang {Guid.NewGuid():N}",
            languages = new[] { "en", "ja" },
            allowLanguagesNotInProfile = false,
            upgradeAllowed = true
        });
        var profileResponse = await _client.ExecuteAsync(profileRequest);
        if (!profileResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedCutoffUnmetChapterAsync: translationprofile POST failed " +
                $"[{(int)profileResponse.StatusCode}] body={profileResponse.Content}");
        }

        int translationProfileId;
        using (var profileDoc = JsonDocument.Parse(profileResponse.Content ?? "{}"))
        {
            translationProfileId = profileDoc.RootElement.GetProperty("id").GetInt32();
        }

        // 2. GET the manga, swap its TranslationProfileId, PUT it back. PUT
        //    round-trips the full resource through Manga.ApplyChanges; issue #96
        //    fix preserves existing.Path if the inbound body omits it, but we
        //    round-trip the full GET body so every field stays canonical.
        var mangaGet = await _client.ExecuteAsync(BuildRequest($"manga/{mangaId}", Method.GET));
        if (!mangaGet.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedCutoffUnmetChapterAsync: manga GET failed " +
                $"[{(int)mangaGet.StatusCode}] body={mangaGet.Content}");
        }

        var mangaResource = JsonNode.Parse(mangaGet.Content).AsObject();
        mangaResource["translationProfileId"] = translationProfileId;

        var mangaPut = BuildRequest($"manga/{mangaId}", Method.PUT);
        mangaPut.AddParameter("application/json", mangaResource.ToJsonString(), ParameterType.RequestBody);
        var mangaPutResponse = await _client.ExecuteAsync(mangaPut);
        if (!mangaPutResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedCutoffUnmetChapterAsync: manga PUT failed " +
                $"[{(int)mangaPutResponse.StatusCode}] body={mangaPutResponse.Content}");
        }

        // 3. Raw-SQLite INSERT into ChapterFiles + UPDATE Chapters. Identifiers
        //    double-quoted for postgres-case-preserve (same pattern as the other
        //    seed helpers). Wrap both writes in a transaction so they fail-fast
        //    together if either trips a schema drift.
        using var connection = OpenDatabase(appDataPath);
        using var transaction = connection.BeginTransaction();

        try
        {
            // INSERT ChapterFile. Schema (001_mangarr_baseline.cs:641-650):
            //   MangaId(NN) + ChapterId(NN) + RelativePath(NN) + Path(NN) +
            //   Size(NN) + DateAdded(NN) + OriginalFilePath(null) +
            //   TranslatedLanguage(null) + ScanlationGroup(null).
            // No JSON columns — no EmbeddedDocumentSettings serialization needed.
            // RETURNING the new id round-trips on both SQLite and postgres (SQLite
            // supports RETURNING since 3.35; the project's bundled SQLite is well
            // above that floor).
            var relativePath = $"Chapter {chapterId}.cbz";
            var fullPath = $"/testkit/cutoff-unmet/manga-{mangaId}/chapter-{chapterId}.cbz";
            int chapterFileId;

            using (var insertFile = connection.CreateCommand())
            {
                insertFile.Transaction = transaction;

                // Branch on SQLite vs postgres for the id-recovery hop (last_insert_rowid
                // vs RETURNING). Mirrors SeedChapterFileAsync L717-744 explicit if/else
                // shape so neither branch carries a dead-write that masks intent (BL-01).
                if (_postgresOptions != null && _postgresOptions.Host.IsNotNullOrWhiteSpace())
                {
                    insertFile.CommandText =
                        "INSERT INTO \"ChapterFiles\" " +
                        "(\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\", " +
                        " \"OriginalFilePath\", \"TranslatedLanguage\", \"ScanlationGroup\") " +
                        "VALUES " +
                        "(@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded, " +
                        " @OriginalFilePath, @TranslatedLanguage, @ScanlationGroup) " +
                        "RETURNING \"Id\";";
                }
                else
                {
                    insertFile.CommandText =
                        "INSERT INTO \"ChapterFiles\" " +
                        "(\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\", " +
                        " \"OriginalFilePath\", \"TranslatedLanguage\", \"ScanlationGroup\") " +
                        "VALUES " +
                        "(@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded, " +
                        " @OriginalFilePath, @TranslatedLanguage, @ScanlationGroup); " +
                        "SELECT last_insert_rowid();";
                }

                AddParam(insertFile, "@MangaId", mangaId);
                AddParam(insertFile, "@ChapterId", chapterId);
                AddParam(insertFile, "@RelativePath", relativePath);
                AddParam(insertFile, "@Path", fullPath);
                AddParam(insertFile, "@Size", 1024L);
                AddParam(insertFile, "@DateAdded", DateTime.UtcNow);
                AddParam(insertFile, "@OriginalFilePath", DBNull.Value);

                // TranslatedLanguage = "en" so the imported file lands on the
                // top-rank language of the new multi-language profile (en > ja);
                // makes the row a genuine "imported in en, ja-upgrade available"
                // below-cutoff case rather than a synthetic stub.
                AddParam(insertFile, "@TranslatedLanguage", "en");
                AddParam(insertFile, "@ScanlationGroup", DBNull.Value);

                var newId = insertFile.ExecuteScalar();
                if (newId == null || newId is DBNull)
                {
                    throw new InvalidOperationException(
                        "TestKit.SeedCutoffUnmetChapterAsync: ChapterFiles INSERT returned no id");
                }

                chapterFileId = Convert.ToInt32(newId);
            }

            // UPDATE Chapter.ChapterFileId + force Monitored=true so the cutoff
            // service's c.Monitored filter holds regardless of any chapter-level
            // monitor toggle the AddManga flow may have applied.
            using (var updateChapter = connection.CreateCommand())
            {
                updateChapter.Transaction = transaction;
                updateChapter.CommandText =
                    "UPDATE \"Chapters\" " +
                    "SET \"ChapterFileId\" = @ChapterFileId, \"Monitored\" = @Monitored " +
                    "WHERE \"Id\" = @ChapterId";
                AddParam(updateChapter, "@ChapterFileId", chapterFileId);
                AddParam(updateChapter, "@Monitored", true);
                AddParam(updateChapter, "@ChapterId", chapterId);

                var updated = updateChapter.ExecuteNonQuery();
                if (updated != 1)
                {
                    throw new InvalidOperationException(
                        $"TestKit.SeedCutoffUnmetChapterAsync: expected 1 Chapters row updated " +
                        $"for id={chapterId}, got {updated}");
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task TriggerPendingReleaseRebuildAsync()
    {
        var getReq = BuildRequest("config/downloadclient", Method.GET);
        var getResponse = await _client.ExecuteAsync(getReq);
        if (!getResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedPendingQueueItemAsync: config/downloadclient GET failed [{(int)getResponse.StatusCode}] body={getResponse.Content}");
        }

        var config = JsonNode.Parse(getResponse.Content).AsObject();

        // Toggle DownloadClientWorkingFolders to a unique value so SaveConfigDictionary
        // detects a change and publishes ConfigSavedEvent.
        config["downloadClientWorkingFolders"] =
            $"_intermediate|_unpack|testkit-{Guid.NewGuid():N}";

        var putReq = BuildRequest("config/downloadclient/1", Method.PUT);
        putReq.AddParameter("application/json", config.ToJsonString(), ParameterType.RequestBody);
        var putResponse = await _client.ExecuteAsync(putReq);
        if (!putResponse.IsSuccessful)
        {
            throw new InvalidOperationException(
                $"TestKit.SeedPendingQueueItemAsync: config/downloadclient PUT failed [{(int)putResponse.StatusCode}] body={putResponse.Content}");
        }
    }

    private DbConnection OpenDatabase(string appDataPath)
    {
        // gh-152 Class 6: branch on backend type. The 4 automation_test_nightly
        // postgres matrix entries (PR #150) configure the child Mangarr to
        // persist to a per-run-uid postgres MainDb — no SQLite file is ever
        // written. When PostgresOptions.Host is populated, open an Npgsql
        // connection to that MainDb instead of looking for mangarr.db on disk.
        // The sqlite path is structurally unchanged.
        if (_postgresOptions != null && _postgresOptions.Host.IsNotNullOrWhiteSpace())
        {
            // Mirror PostgresDatabase.GetConnectionString (the canonical Mangarr
            // postgres builder) plus the per-run MainDb. Enlist=false +
            // IncludeErrorDetail=true match the test-common helper verbatim.
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = _postgresOptions.Host,
                Port = _postgresOptions.Port,
                Username = _postgresOptions.User,
                Password = _postgresOptions.Password,
                Database = _postgresOptions.MainDb,
                Enlist = false,
                IncludeErrorDetail = true
            };

            var npgsqlConnection = new NpgsqlConnection(builder.ConnectionString);
            npgsqlConnection.Open();
            return npgsqlConnection;
        }

        // T-19-01: derive the DB path from the harness-owned AppData dir only —
        // never a test-controlled string that could escape the per-fixture
        // sandbox. Phase 18 D-05 wipes this file per fixture so seeded rows
        // never leak.
        var dbPath = Path.Combine(appDataPath, "mangarr.db");
        if (!File.Exists(dbPath))
        {
            throw new InvalidOperationException(
                $"TestKit: backend SQLite file not found at {dbPath} — has the backend booted?");
        }

        var connection = new SQLiteConnection($"Data Source={dbPath};");
        connection.Open();
        return connection;
    }

    // gh-152 Class 6: AddWithValue is a concrete-class convenience on
    // SQLiteParameterCollection / NpgsqlParameterCollection — not part of the
    // abstract DbParameterCollection contract. This helper builds a parameter
    // via DbCommand.CreateParameter so the seed INSERTs work on either provider
    // unchanged.
    private static void AddParam(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private RestRequest BuildRequest(string resource, Method method)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("X-Api-Key", _apiKey); // copied verbatim from NzbDroneRunner.cs:76
        return request;
    }

    /// <summary>
    /// GH #169 follow-up resolution — exercise the
    /// <c>POST /api/v5/indexer/action/{name}</c> endpoint surface directly via the
    /// API (independent of any provider-action-bearing UI field). The IndexerController
    /// endpoint (ProviderControllerBase.RequestAction) GETs the indexer resource by id
    /// from the seeded MangaDex row, POSTs it back to the action route, and the
    /// indexer's <c>RequestAction(name, query)</c> dispatches. MangaDex inherits the
    /// IndexerBase default (returns null), so the controller wraps null in a
    /// ContentHttpResult with HTTP 200 + body "null". That is the deterministic
    /// proof the controller dispatch path is wired end-to-end on the v5 surface,
    /// regardless of whether a providerAction-bearing UI field is rendered.
    ///
    /// Returns the full IRestResponse so the caller can assert on status + body
    /// shape per the fixture's contract.
    /// </summary>
    public async Task<IRestResponse> RequestIndexerActionAsync(int indexerId, string actionName)
    {
        var getResponse = await ExecuteWithStartupRetryAsync(
            nameof(RequestIndexerActionAsync),
            $"indexer/{indexerId} GET",
            () => BuildRequest($"indexer/{indexerId}", Method.GET));

        var resourceBody = getResponse.Content;
        if (string.IsNullOrEmpty(resourceBody))
        {
            throw new InvalidOperationException(
                $"TestKit.RequestIndexerActionAsync: indexer/{indexerId} GET returned empty body — seed regression?");
        }

        // ProviderControllerBase.RequestAction expects the full TProviderResource as
        // [FromBody]. Pass the body verbatim with the application/json content type;
        // RestSharp 106 AddJsonBody(string) would double-encode the string.
        var actionRequest = BuildRequest($"indexer/action/{actionName}", Method.POST);
        actionRequest.AddParameter("application/json", resourceBody, ParameterType.RequestBody);
        return await _client.ExecuteAsync(actionRequest);
    }
}
