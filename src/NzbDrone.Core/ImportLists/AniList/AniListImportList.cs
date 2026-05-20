using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.AniList.Resource;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 Task 3 — AniList list ThingiProvider plugin.
    //
    // Extends OAuthAwareImportListBase<AniListImportListSettings> (Plan 27-01) to inherit:
    //   * RefreshTokenIfNecessary() template (Trakt.cs:127-133 + D-05 SemaphoreSlim wrap)
    //   * Fetch() pre-call refresh-check (base wraps RefreshTokenIfNecessary + base.Fetch)
    //   * Per-Definition.Id semaphore registry (Pitfall 9 mitigation)
    //
    // Pattern source (composite):
    //   * Registration shape: src/NzbDrone.Core/ImportLists/MangaDex/MangaDexImportList.cs (Plan 27-02
    //     sibling-canonical provider).
    //   * OAuth RequestAction surface (D-07 paste-pin paste-back UX): two actions
    //       - startOAuth → returns { OauthUrl: "https://anilist.co/api/v2/oauth/pin?..." } so the FE
    //         opens the pin authorize URL in a new tab.
    //       - getAuthPin → exchanges the pasted pin for the 1-year JWT and persists it on Settings.
    //   * Trakt-canonical envelope shape (Trakt.cs:102-125) — return object literal with the OauthUrl
    //     property; FE useOAuth hook reads it as the launch URL.
    //
    // D-07 paste-pin paste-back UX: NO redirect-callback wiring (AniList's pin flow lives
    // entirely in the user's browser tab — they read the pin off the page and paste it into
    // the AniListPinModal at frontend/src/Settings/ImportLists/AniList/AniListPinModal.tsx).
    //
    // D-10 single-select Status enum: Settings.Status flows verbatim into the GraphQL
    // $status variable via AniListImportListRequestGenerator.GetListItems().
    //
    // RefreshToken() is a no-op for AniList (1-year JWT, no refresh-token grant per RESEARCH
    // §STACK §Surface 2). Planner choice per CONTEXT lines 28-29 + Plan 27-03 Test 6:
    // log-and-noop preferred over throw, so the inherited Fetch() pre-call refresh check
    // doesn't crash mid-sync when Settings.Expires approaches. On 401 from AniList (token
    // actually invalidated by upstream) the inherited HttpImportListBase exception ladder
    // records RecordFailure and surfaces the actionable "Re-authenticate" error to the FE
    // banner; the user re-runs the pin flow from Settings.
    //
    // Shared transport reuse: AniListImportListRequestGenerator builds the GraphQL body and
    // wraps it in a HttpRequest pointed at https://graphql.anilist.co; the overridden
    // FetchImportListResponse below detects that endpoint and routes the body through
    // IAniListGraphQlTransport.Post<...> instead of _httpClient.Execute(...). This preserves
    // the transport's RateLimitKey="anilist" budget honoring (the transport sets it at
    // AniListGraphQlTransport.cs:50) WITHOUT creating a parallel transport class at the
    // ImportList tier.
    public class AniListImportList : OAuthAwareImportListBase<AniListImportListSettings>
    {
        private readonly IAniListImportListProxy _proxy;
        private readonly IAniListGraphQlTransport _transport;
        private readonly IImportListRepository _importListRepository;

        public AniListImportList(
            IAniListImportListProxy proxy,
            IAniListGraphQlTransport transport,
            IImportListRepository importListRepository,
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            ILocalizationService localizationService,
            Logger logger)
            : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
            _proxy = proxy;
            _transport = transport;
            _importListRepository = importListRepository;
        }

        public override string Name => "AniList";

        public override ImportListType ListType => ImportListType.AniList;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        public override IImportListRequestGenerator GetRequestGenerator()
            => new AniListImportListRequestGenerator { Settings = Settings };

        public override IParseImportListResponse GetParser()
            => new AniListImportListParser();

        // D-07 Sonarr-canonical OAuth action surface (Trakt.cs:102-125 verbatim shape adapted
        // for AniList's pin flow). Two actions:
        //
        //   * "startOAuth" — returns the AniList pin authorize URL the FE opens in a new tab.
        //     Envelope: { OauthUrl: "https://anilist.co/api/v2/oauth/pin?client_id=..." }.
        //     Mirrors Trakt.cs:104-111 envelope shape; the FE useOAuth hook reads OauthUrl
        //     verbatim and calls window.open(...).
        //
        //   * "getAuthPin" — exchanges the pin the user pasted in AniListPinModal for the
        //     1-year JWT. Persists AccessToken + Expires + AuthUser on Settings. Envelope:
        //     { accessToken, expires, authUser } (the FE useOAuth result.* keys round-trip
        //     into the Settings POCO via the form-input onChange handler — see
        //     OAuthInput.tsx:42-46).
        //
        // T-V7: the response envelope from getAuthPin DOES include `accessToken` because the
        // FE useOAuth hook persists it via onChange to the Settings.AccessToken hidden field.
        // The substrate redacts hidden fields on outbound /api/v5/importlist GET schema calls
        // (ProviderControllerBase strips Hidden=HiddenType.Hidden fields). The one-way
        // outbound-from-server response on this action surface is acceptable per Phase 26
        // substrate contract.
        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                // Pin URL construction is deterministic — no HTTP call. The proxy honors the
                // verbatim CONTEXT line 23 URL shape:
                //   https://anilist.co/api/v2/oauth/pin?client_id={id}&response_type=code
                var oauthUrl = _proxy.GetPinAuthorizeUrl(Settings);
                return new { OauthUrl = oauthUrl };
            }

            if (action == "getAuthPin")
            {
                if (query == null || !query.TryGetValue("pin", out var pin) || string.IsNullOrWhiteSpace(pin))
                {
                    return new { success = false, error = "Pin is required. Paste the pin issued by AniList in the new tab into the modal." };
                }

                try
                {
                    var response = _proxy.ExchangePinForToken(pin, Settings);
                    if (response == null || string.IsNullOrWhiteSpace(response.AccessToken))
                    {
                        return new { success = false, error = "AniList returned an empty token response. Verify your Client ID + Client Secret and re-issue a new pin." };
                    }

                    Settings.AccessToken = response.AccessToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(response.ExpiresIn);

                    // AuthUser: AniList's MediaListCollection(userName: ...) query requires the
                    // username. Resolve it via a follow-up GraphQL Viewer query against the
                    // shared transport — the AccessToken just issued is now valid for the
                    // bearer-authenticated Viewer call.
                    Settings.AuthUser = ResolveAuthUserFromAccessToken(response.AccessToken);

                    // Fail the OAuth flow if AuthUser couldn't be resolved — saving the
                    // partial settings would leave subsequent list fetches broken
                    // (MediaListCollection requires userName). Surface a clear retryable
                    // error rather than reporting false success.
                    if (string.IsNullOrWhiteSpace(Settings.AuthUser))
                    {
                        return new
                        {
                            success = false,
                            error = "AniList token exchange succeeded but the Viewer { name } follow-up query failed to resolve the username. Re-issue a new pin and try again; if the problem persists, file a bug with the access-token expiry."
                        };
                    }

                    return new
                    {
                        accessToken = Settings.AccessToken,
                        expires = Settings.Expires,
                        authUser = Settings.AuthUser
                    };
                }
                catch (HttpException ex)
                {
                    // T-V7: NEVER include the pin or token in error payload. Sonarr-canonical
                    // shape — only the HTTP status + exception message.
                    return new
                    {
                        success = false,
                        statusCode = (int?)ex.Response?.StatusCode ?? 0,
                        error = ex.Message
                    };
                }
            }

            return new { };
        }

        // CONTEXT line 28-29 + Plan 27-03 Test 6: AniList does NOT issue refresh tokens
        // (1-year JWT). Override is a no-op that logs a warning so any callers downstream
        // (e.g., base.Fetch's pre-call RefreshTokenIfNecessary template) can proceed without
        // throwing mid-sync. On 401 from AniList (actual upstream invalidation) the inherited
        // exception ladder surfaces the actionable error to the FE banner.
        protected override void RefreshToken()
        {
            _logger.Warn(
                "AniList tokens are 1-year JWTs; refresh not supported. Re-authenticate via Settings → ImportLists when the current token expires.");
        }

        // Route AniList GraphQL requests through the SHARED IAniListGraphQlTransport (Phase 26
        // Plan 26-02). The request generator's HttpRequest carries the GraphQL body in
        // request.HttpRequest.ContentSummary / GetContent; we extract it and call
        // transport.Post<AniListMediaListResource> directly. Mock-on-_transport unit tests
        // (Plan 27-03 Task 3 Test 4) assert this routing path.
        //
        // The transport's response is serialized back to JSON and stuffed into a fake
        // HttpResponse so the substrate's ImportListResponse / parser chain works unchanged.
        // Net effect: SourceKey="anilist" budget honored (transport sets it), parser receives
        // JSON matching the AniListGraphQlResponse<AniListMediaListResource> shape Task 2's
        // parser expects.
        protected override ImportListResponse FetchImportListResponse(ImportListRequest request)
        {
            if (request?.HttpRequest?.Url?.FullUri?.StartsWith(AniListMangaApi.GraphQlEndpoint, StringComparison.OrdinalIgnoreCase) == true)
            {
                var body = request.HttpRequest.ContentData != null
                    ? System.Text.Encoding.UTF8.GetString(request.HttpRequest.ContentData)
                    : string.Empty;

                var graphQlResponse = _transport.Post<AniListMediaListResource>(body);
                var content = JsonConvert.SerializeObject(graphQlResponse ?? new AniListGraphQlResponse<AniListMediaListResource>());

                var fakeResponse = new HttpResponse(
                    request.HttpRequest,
                    new HttpHeader { ContentType = "application/json" },
                    content,
                    HttpStatusCode.OK);
                return new ImportListResponse(request, fakeResponse);
            }

            return base.FetchImportListResponse(request);
        }

        // After the pin exchange completes, run a follow-up `Viewer { name }` GraphQL query
        // against the shared transport using the just-issued AccessToken. AniList's
        // MediaListCollection query is keyed on username — the username is NOT carried in the
        // OAuth pin exchange response, so we must resolve it explicitly here.
        //
        // The transport's RateLimitKey="anilist" SHARED with AniListMetadataSource correctly
        // covers this fire-once-per-pin-exchange call (one extra request per year per
        // ImportList — well inside the 30 req/min budget).
        //
        // T-V7: the bearer-token attach happens inside this method, not in the proxy; the
        // transport DOES NOT log bearer values (AniListGraphQlTransport.cs:62 only logs the
        // 429 warning string). The follow-up Viewer query is the simplest reliable resolver
        // — base64-decoding the JWT middle segment is fragile across token-format changes.
        private string ResolveAuthUserFromAccessToken(string accessToken)
        {
            try
            {
                var viewerQuery = @"query { Viewer { name } }";
                var body = JsonConvert.SerializeObject(new { query = viewerQuery });

                // Bearer-token attach: the shared transport's Post<T> overload does not accept
                // headers, so we issue the Viewer query through _httpClient directly. This is
                // the SINGLE place in the ImportList tier where _httpClient.Execute fires;
                // every other request flows through the transport. The fire-once-per-pin-exchange
                // path means the SourceKey budget would be over-applied if we also set
                // RateLimitKey="anilist" here, but consistency with the transport's contract is
                // worth the trade — set it explicitly so the 30 req/min ceiling is honored.
                var request = new HttpRequest(AniListMangaApi.GraphQlEndpoint)
                {
                    Method = System.Net.Http.HttpMethod.Post,
                    RateLimitKey = "anilist"
                };
                request.Headers["Content-Type"] = "application/json";
                request.Headers["User-Agent"] = $"Mangarr/{BuildInfo.Version.ToString(2)}";
                request.Headers["Authorization"] = $"Bearer {accessToken}";
                request.SetContent(body);

                var response = _httpClient.Post(request);
                if (response?.Content == null)
                {
                    return string.Empty;
                }

                var envelope = JsonConvert.DeserializeObject<AniListGraphQlResponse<AniListViewerResponse>>(response.Content);
                return envelope?.Data?.Viewer?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                // T-V7: log only the exception message — NEVER the access token.
                _logger.Warn(ex, "Could not resolve AniList Viewer.name after auth completion; the user must populate AuthUser manually");
                return string.Empty;
            }
        }

        // Minimal DTO for the Viewer { name } follow-up — local-only, doesn't need to leak
        // into the shared MetadataSource.AniList.Resource namespace.
        private sealed class AniListViewerResponse
        {
            public AniListViewer Viewer { get; set; }
        }

        private sealed class AniListViewer
        {
            public string Name { get; set; }
        }
    }
}
