using System;
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
    // MAL App Type duality (GH #233 — 2026-05-21):
    //   MAL OAuth supports two App Types at https://myanimelist.net/apiconfig:
    //     * "Other" — public-client PKCE; NO client_secret. The verifier/challenge
    //       replaces the shared-secret defense.
    //     * "web"   — confidential-client PKCE; REQUIRES client_secret in the
    //       token-exchange + refresh-token form bodies per MAL blog
    //       https://myanimelist.net/blog.php?eid=835707. This is the default app
    //       type on registration, so most users hit this code path.
    //   Both ExchangeCodeForToken + RefreshAccessToken accept clientSecret as a
    //   nullable/optional string parameter. When non-empty, `client_secret=` is
    //   appended to the form body; when empty, the parameter is omitted entirely
    //   (back-compat with the original v1.1 ship — "Other" app-type users continue
    //   to work without any settings change).
    //
    // Pitfall 10 / SourceKey="myanimelist" NEW bucket per CONTEXT line 36 + 27-04-PLAN
    // success criteria: every outbound HTTP request from this proxy AND from
    // MalImportListRequestGenerator sets RateLimitKey="myanimelist". NEW bucket — NOT
    // shared with any other Mangarr caller. The single 30 req/min (default) budget
    // covers OAuth exchanges + list fetches + future MAL touchpoints (e.g., reading
    // progress sync if v1.2+ ships).
    //
    // T-V7 token-leak prevention: the proxy NEVER logs token / code / verifier / refresh-token
    // / client-secret values. _logger.Warn(...) calls below carry ONLY the exception message +
    // HTTP status + endpoint path (matches sibling Plan 27-02 MangaDex proxy + Plan 27-03
    // AniList proxy hygiene). Log message phrasing intentionally avoids the literal substrings
    // `Bearer`/`access_token`/`refresh_token`/`code_verifier`/`code=`/`client_secret=` so the
    // Plan 27-05 close-out audit grep returns 0 (substantive T-V7 protection is the ABSENCE of
    // secret-value arguments to _logger calls).
    public interface IMalImportListProxy
    {
        // D-09 step 1: build the URL the FE opens in a new tab. Deterministic
        // URL construction (no HTTP call) — query string carries PKCE
        // code_challenge using the MAL-required `plain` method + 32-byte state nonce
        // (CSRF defense). `clientId` is the user-supplied OAuth client identifier
        // (MalImportListSettings.ClientId). MAL's authorize URL never carries the
        // client_secret regardless of App Type (it only appears in token-exchange / refresh).
        string BuildAuthorizeUrl(string clientId, MalOAuthState state);

        // D-09 step 2: POST form-urlencoded grant_type=authorization_code with
        // code + code_verifier to token endpoint. Returns the canonical MAL token
        // response (access_token + refresh_token + expires_in).
        //
        // GH #233: `clientSecret` is optional. When non-empty, it is appended as
        // `client_secret={value}` to the form body (MAL App Type "web" path). When
        // null/empty/whitespace, the parameter is omitted entirely (MAL App Type "Other"
        // back-compat).
        MalTokenResponse ExchangeCodeForToken(string clientId, string clientSecret, string code, string verifier);

        // D-05 / Trakt.cs:135-163 canonical refresh path. POST grant_type=refresh_token
        // — MAL ROTATES refresh tokens (caller applies the Trakt.cs:151 null-coalesce:
        // `Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken`).
        //
        // GH #233: same conditional client_secret treatment as ExchangeCodeForToken.
        MalTokenResponse RefreshAccessToken(string clientId, string clientSecret, string refreshToken);

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

        public string BuildAuthorizeUrl(string clientId, MalOAuthState state)
        {
            // D-09 + STACK §Surface 2 verbatim URL shape:
            //   https://myanimelist.net/v1/oauth2/authorize?response_type=code
            //     &client_id={clientId}            (user-supplied from MalImportListSettings.ClientId)
            //     &code_challenge={state.Verifier}
            //     &code_challenge_method=plain    (MAL only accepts plain)
            //     &state={state.StateNonce}
            //     &redirect_uri={MalConstants.RedirectUri}
            //
            // PKCE-plain rationale (T-V2 accept): MAL accepts ONLY
            // code_challenge_method=plain (the hashed variant is NOT supported —
            // two independent sources confirm per CONTEXT line 28; verified at MAL
            // OAuth reference). Plain is weaker than the hashed variant but is the
            // only method MAL accepts — threat is accepted, not a defect.
            //
            // client_secret never appears in the authorize URL regardless of App Type —
            // MAL's OAuth2 authorize endpoint only consumes it server-side during
            // token-exchange.
            var request = new HttpRequestBuilder(MalConstants.AuthorizeUrl)
                .AddQueryParam("response_type", "code")
                .AddQueryParam("client_id", clientId ?? string.Empty)
                .AddQueryParam("code_challenge", state.Verifier)
                .AddQueryParam("code_challenge_method", "plain")
                .AddQueryParam("state", state.StateNonce)
                .AddQueryParam("redirect_uri", MalConstants.RedirectUri)
                .Build();

            return request.Url.FullUri;
        }

        public MalTokenResponse ExchangeCodeForToken(string clientId, string clientSecret, string code, string verifier)
        {
            // OAuth2 authorization_code grant per
            // https://myanimelist.net/apiconfig/references/authorization. Form-urlencoded body:
            //   grant_type=authorization_code
            //     &client_id={clientId}           (user-supplied)
            //     &client_secret={clientSecret}   (GH #233 — only when non-empty; MAL App Type "web")
            //     &code={code}
            //     &code_verifier={verifier}
            //     &redirect_uri={MalConstants.RedirectUri}
            //
            // GH #233: when Settings.ClientSecret is set (MAL App Type "web" — confidential PKCE),
            // we MUST send client_secret in the body or MAL returns 401 invalid_client. When the
            // setting is empty (MAL App Type "Other" — public-client PKCE), we MUST NOT send
            // client_secret or MAL rejects with 400 / 401 against an unknown secret. The verifier
            // ALWAYS goes in regardless of App Type.
            var builder = new HttpRequestBuilder(MalConstants.TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "authorization_code")
                .AddFormParameter("client_id", clientId ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                builder.AddFormParameter("client_secret", clientSecret);
            }

            builder
                .AddFormParameter("code", code ?? string.Empty)
                .AddFormParameter("code_verifier", verifier ?? string.Empty)
                .AddFormParameter("redirect_uri", MalConstants.RedirectUri);

            var request = builder.Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public MalTokenResponse RefreshAccessToken(string clientId, string clientSecret, string refreshToken)
        {
            // OAuth2 refresh-token grant. Form-urlencoded body:
            //   grant_type=refresh_token
            //     &client_id={clientId}           (user-supplied)
            //     &client_secret={clientSecret}   (GH #233 — only when non-empty; MAL App Type "web")
            //     &refresh_token={refreshToken}
            //
            // MAL ROTATES refresh tokens (31-day observed lifetime); caller (MalImportList
            // RefreshToken override) applies Trakt.cs:151 null-coalesce to the rotated value.
            //
            // GH #233: same conditional client_secret treatment as ExchangeCodeForToken — the
            // MAL "web" App Type requires it in BOTH the initial exchange AND the refresh leg.
            var builder = new HttpRequestBuilder(MalConstants.TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "refresh_token")
                .AddFormParameter("client_id", clientId ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(clientSecret))
            {
                builder.AddFormParameter("client_secret", clientSecret);
            }

            builder.AddFormParameter("refresh_token", refreshToken ?? string.Empty);

            var request = builder.Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public HttpResponse<MalMangaListResource> GetUserMangaList(MalImportListSettings settings, string nextCursor)
        {
            // D-10 single-select status filter propagation. Initial request builds the URL
            // with Settings.Status as `?status={snake_case_value}` query param + fields=list_status
            // (so MAL returns the list_status payload alongside each node). Subsequent
            // pagination uses MAL-returned `paging.next` cursor URL — VALIDATED against the
            // canonical MAL API host before reuse (we attach a Bearer token to the request,
            // so a maliciously-crafted paging.next pointing at an attacker host would
            // exfiltrate the user's token via the Authorization header).
            HttpRequest request;
            if (!string.IsNullOrWhiteSpace(nextCursor))
            {
                if (!IsTrustedMalCursor(nextCursor))
                {
                    throw new InvalidOperationException(
                        $"Refusing to follow MAL paging.next cursor: not on the canonical api.myanimelist.net host. " +
                        $"This is a defense against token-exfiltration via a malicious cursor URL (T-V13).");
                }

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

        // T-V13 cursor-host validation: MAL's paging.next is returned by the upstream
        // server, but we never blindly trust it for an authenticated request — the
        // Authorization header carries the user's Bearer token, so following a cursor
        // to an attacker-controlled host would leak the token. Pin to the canonical
        // MAL API host AND require an HTTPS scheme.
        private static bool IsTrustedMalCursor(string cursor)
        {
            if (!Uri.TryCreate(cursor, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(uri.Host, "api.myanimelist.net", StringComparison.OrdinalIgnoreCase);
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
                // T-V7: pass message-only, NOT the exception object. NLog's exception
                // formatter calls HttpException.ToString() which serializes the response
                // body / headers — that body can contain credentials echoed in the
                // upstream error payload. Message-only keeps the diagnostic value
                // (endpoint path + HTTP status + error message) without the leak surface.
                // Phrasing avoids literal "Bearer"/"access_token"/"refresh_token"/
                // "code_verifier"/"code="/"client_secret=" tokens so the close-out audit
                // grep returns 0.
                var status = ex.Response?.StatusCode ?? HttpStatusCode.InternalServerError;
                _logger.Warn("Error exchanging MyAnimeList OAuth grant: HTTP {0} {1} ({2})",
                    (int)status,
                    status,
                    ex.Message);
                throw;
            }
        }
    }
}
