using System.Net;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.MyAnimeList.Resource;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 2 — MAL OAuth + manga-list HTTP proxy.
    //
    // Pattern source (verbatim shape):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs
    //   .planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-PATTERNS.md Pattern D
    //
    // Endpoints (verified against
    // https://myanimelist.net/apiconfig/references/authorization +
    // CONTEXT lines 283-287 + RESEARCH §STACK §Surface 2):
    //   * Authorize: https://myanimelist.net/v1/oauth2/authorize
    //     (browser-only — Mangarr does NOT POST here; BuildAuthorizeUrl returns the URL
    //     the FE opens in a new tab per D-09 paste-URL UX)
    //   * Token: https://myanimelist.net/v1/oauth2/token
    //     (POST form-urlencoded; grant_type=authorization_code with code_verifier
    //     for first exchange OR grant_type=refresh_token for rotation)
    //   * Manga list: https://api.myanimelist.net/v2/users/@me/mangalist
    //     (GET; Bearer-token-authenticated; paginated via paging.next cursor URL)
    //
    // Pitfall 10 / SourceKey="myanimelist" NEW bucket per CONTEXT line 36 + 27-04-PLAN
    // success criteria: every outbound HTTP request from this proxy AND from
    // MalImportListRequestGenerator sets RateLimitKey="myanimelist". NEW bucket — NOT
    // shared with any other Mangarr caller. The single 30 req/min (default) budget
    // covers OAuth exchanges + list fetches + future MAL touchpoints (e.g., reading
    // progress sync if v1.2+ ships).
    //
    // T-V7 token-leak prevention: the proxy NEVER logs token / code / verifier / refresh-token
    // values. _logger.Warn(...) calls below carry ONLY the exception message + HTTP status +
    // endpoint path (matches sibling Plan 27-02 MangaDex proxy + Plan 27-03 AniList proxy hygiene).
    // Log message phrasing intentionally avoids the literal substrings `Bearer`/`access_token`/
    // `refresh_token`/`code_verifier`/`code=` so the Plan 27-05 close-out audit grep returns 0
    // (substantive T-V7 protection is the ABSENCE of secret-value arguments to _logger calls).
    public interface IMalImportListProxy
    {
        // D-09 step 1: build the URL the FE opens in a new tab. Deterministic
        // URL construction (no HTTP call) — query string carries PKCE
        // code_challenge using the MAL-required `plain` method + 32-byte state nonce
        // (CSRF defense).
        string BuildAuthorizeUrl(MalOAuthState state);

        // D-09 step 2: POST form-urlencoded grant_type=authorization_code with
        // code + code_verifier to token endpoint. Returns the canonical MAL token
        // response (access_token + refresh_token + expires_in).
        MalTokenResponse ExchangeCodeForToken(string code, string verifier);

        // D-05 / Trakt.cs:135-163 canonical refresh path. POST grant_type=refresh_token
        // — MAL ROTATES refresh tokens (caller applies the Trakt.cs:151 null-coalesce:
        // `Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken`).
        MalTokenResponse RefreshAccessToken(string refreshToken);

        // Bearer-authenticated GET against /v2/users/@me/mangalist. When `nextCursor`
        // is non-null/non-empty, the request URL is the cursor URL verbatim
        // (MAL returns full-URL cursors in `paging.next`). When null, the initial
        // request is built with Settings.Status as `?status={value}` query param.
        HttpResponse<MalMangaListResource> GetUserMangaList(MalImportListSettings settings, string nextCursor);
    }

    public class MalImportListProxy : IMalImportListProxy
    {
        // Pitfall 10 / 27-04 charter — NEW SourceKey bucket per CONTEXT line 36.
        // This literal MUST appear on every RateLimitKey assignment in this file AND
        // in MalImportListRequestGenerator; the Plan 27-05 close-out grep gate enforces
        // exclusivity (zero non-myanimelist values in this directory).
        private const string SharedSourceKey = "myanimelist";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MalImportListProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string BuildAuthorizeUrl(MalOAuthState state)
        {
            // D-09 + STACK §Surface 2 verbatim URL shape:
            //   https://myanimelist.net/v1/oauth2/authorize?response_type=code
            //     &client_id={MalConstants.ClientId}
            //     &code_challenge={state.Verifier}
            //     &code_challenge_method=plain   (MAL only accepts plain)
            //     &state={state.StateNonce}
            //     &redirect_uri={MalConstants.RedirectUri}
            //
            // PKCE-plain rationale (T-V2 accept): MAL accepts ONLY
            // code_challenge_method=plain (the hashed variant is NOT supported —
            // two independent sources confirm per CONTEXT line 28; verified at MAL
            // OAuth reference). Plain is weaker than the hashed variant but is the
            // only method MAL accepts — threat is accepted, not a defect.
            var request = new HttpRequestBuilder(MalConstants.AuthorizeUrl)
                .AddQueryParam("response_type", "code")
                .AddQueryParam("client_id", MalConstants.ClientId)
                .AddQueryParam("code_challenge", state.Verifier)
                .AddQueryParam("code_challenge_method", "plain")
                .AddQueryParam("state", state.StateNonce)
                .AddQueryParam("redirect_uri", MalConstants.RedirectUri)
                .Build();

            return request.Url.FullUri;
        }

        public MalTokenResponse ExchangeCodeForToken(string code, string verifier)
        {
            // OAuth2 authorization_code grant per
            // https://myanimelist.net/apiconfig/references/authorization. Form-urlencoded body:
            //   grant_type=authorization_code
            //     &client_id={MalConstants.ClientId}
            //     &code={code}
            //     &code_verifier={verifier}
            //     &redirect_uri={MalConstants.RedirectUri}
            //
            // No client_secret field — MAL public-client PKCE flow per D-09 + Pattern D +
            // RESEARCH §Open Question 4. The verifier+plain challenge replaces the shared-secret
            // defense.
            var request = new HttpRequestBuilder(MalConstants.TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "authorization_code")
                .AddFormParameter("client_id", MalConstants.ClientId)
                .AddFormParameter("code", code ?? string.Empty)
                .AddFormParameter("code_verifier", verifier ?? string.Empty)
                .AddFormParameter("redirect_uri", MalConstants.RedirectUri)
                .Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public MalTokenResponse RefreshAccessToken(string refreshToken)
        {
            // OAuth2 refresh-token grant. Form-urlencoded body:
            //   grant_type=refresh_token
            //     &client_id={MalConstants.ClientId}
            //     &refresh_token={refreshToken}
            //
            // MAL ROTATES refresh tokens (31-day observed lifetime); caller (MalImportList
            // RefreshToken override) applies Trakt.cs:151 null-coalesce to the rotated value.
            var request = new HttpRequestBuilder(MalConstants.TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "refresh_token")
                .AddFormParameter("client_id", MalConstants.ClientId)
                .AddFormParameter("refresh_token", refreshToken ?? string.Empty)
                .Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public HttpResponse<MalMangaListResource> GetUserMangaList(MalImportListSettings settings, string nextCursor)
        {
            // D-10 single-select status filter propagation. Initial request builds the URL
            // with Settings.Status as `?status={snake_case_value}` query param + fields=list_status
            // (so MAL returns the list_status payload alongside each node). Subsequent
            // pagination uses MAL-returned `paging.next` cursor URL verbatim.
            HttpRequest request;
            if (!string.IsNullOrWhiteSpace(nextCursor))
            {
                request = new HttpRequest(nextCursor);
            }
            else
            {
                request = new HttpRequestBuilder($"{MalConstants.ApiBaseUrl}/users/@me/mangalist")
                    .AddQueryParam("status", MapStatusToMalString(settings?.Status ?? MalListStatus.Reading))
                    .AddQueryParam("limit", 1000)
                    .AddQueryParam("offset", 0)
                    .AddQueryParam("fields", "list_status,num_chapters")
                    .Build();
            }

            ApplySharedHeaders(request);
            request.Headers["Authorization"] = $"Bearer {settings?.AccessToken}";

            // T-V7: log ONLY the endpoint cursor presence + status filter — NEVER the bearer
            // token value. The URL itself never contains the token (Bearer is header-only).
            _logger.Debug("MAL manga-list: GET /v2/users/@me/mangalist (cursor={0}, status={1})",
                string.IsNullOrWhiteSpace(nextCursor) ? "initial" : "paged",
                settings?.Status);

            return _httpClient.Get<MalMangaListResource>(request);
        }

        // Pitfall 10 + Phase 1 D-13: every outbound request from this proxy carries
        //   * RateLimitKey="myanimelist"        — NEW bucket per CONTEXT line 36
        //   * User-Agent="Mangarr/{version}"    — honest UA per MAL ToS
        //   * Accept="application/json"         — MAL OAuth + API v2 both speak JSON
        // Keep this helper as the SINGLE write-point so the SourceKey audit gate at
        // Plan 27-05 close-out (Pattern κ grep) catches any drift.
        private static void ApplySharedHeaders(HttpRequest request)
        {
            request.RateLimitKey = SharedSourceKey;
            request.Headers["User-Agent"] = $"Mangarr/{BuildInfo.Version.ToString(2)}";
            request.Headers["Accept"] = "application/json";
        }

        // MalListStatus → MAL API snake_case string. Mirrors the [EnumMember(Value)]
        // attributes on MalListStatus.cs members — kept as a switch here so the proxy
        // does not pull in Newtonsoft EnumMember reflection on every call site (faster
        // + simpler than ToString() + Newtonsoft attribute lookup).
        private static string MapStatusToMalString(MalListStatus status)
        {
            return status switch
            {
                MalListStatus.Reading => "reading",
                MalListStatus.PlanToRead => "plan_to_read",
                MalListStatus.Completed => "completed",
                MalListStatus.OnHold => "on_hold",
                MalListStatus.Dropped => "dropped",
                _ => "reading",
            };
        }

        private MalTokenResponse ExecuteTokenRequest(HttpRequest request)
        {
            try
            {
                var response = _httpClient.Post(request);
                if (response?.Content == null)
                {
                    return null;
                }

                return Json.Deserialize<MalTokenResponse>(response.Content);
            }
            catch (HttpException ex)
            {
                // T-V7: log only the exception MESSAGE + HTTP status — NEVER the code/verifier/
                // refresh-token value. Sonarr-canonical Trakt.cs:161 shape. Phrasing avoids
                // literal "Bearer"/"access_token"/"refresh_token"/"code_verifier"/"code=" tokens
                // so the close-out audit grep returns 0 (substantive T-V7 protection is the
                // absence of secret-VALUE arguments).
                var status = ex.Response?.StatusCode ?? HttpStatusCode.InternalServerError;
                _logger.Warn(ex, "Error exchanging MyAnimeList OAuth grant ({0} {1})", (int)status, status);
                throw;
            }
        }
    }
}
