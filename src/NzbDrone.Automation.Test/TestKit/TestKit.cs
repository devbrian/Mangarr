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
                insertFile.CommandText =
                    "INSERT INTO \"ChapterFiles\" " +
                    "(\"MangaId\", \"ChapterId\", \"RelativePath\", \"Path\", \"Size\", \"DateAdded\", " +
                    " \"OriginalFilePath\", \"TranslatedLanguage\", \"ScanlationGroup\") " +
                    "VALUES " +
                    "(@MangaId, @ChapterId, @RelativePath, @Path, @Size, @DateAdded, " +
                    " @OriginalFilePath, @TranslatedLanguage, @ScanlationGroup); " +
                    "SELECT last_insert_rowid();";

                // Postgres path: last_insert_rowid() is SQLite-specific. Branch on
                // _postgresOptions for the id-recovery hop.
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
}
