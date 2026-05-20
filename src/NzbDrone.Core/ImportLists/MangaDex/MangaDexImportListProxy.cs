using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.MangaDex.Resource;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    // Phase 27 Plan 27-02 Task 2 — MangaDex OAuth + follows-list HTTP proxy.
    //
    // Pattern source (verbatim shape):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs
    //
    // Endpoints (verified — RESEARCH §STACK §Surface 2 OAuth-per-provider table):
    //   * Token: POST https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token
    //     (Keycloak OIDC; supports grant_type=password (D-08 verify-credentials probe) +
    //     grant_type=refresh_token (D-08 lazy refresh per Plan 27-01 base template))
    //   * Follows: GET https://api.mangadex.org/user/follows/manga?limit={N}&offset={N}
    //     (Bearer-token-authenticated; paginated; 100/page max — Pitfall 5)
    //
    // Pitfall 10 HARD RULE: every outbound HTTP request from this proxy AND from
    // MangaDexImportListRequestGenerator sets RateLimitKey="mangadex" SHARED with
    //   * MangaDexMetadataSource (Phase 2)
    //   * MangaDexIndexer (Phase 3)
    //   * in-process image downloader (Phase 4)
    //   * future MangaDex callers (Comix runtime signer, etc.)
    // The single per-SourceKey budget (40 req/min default; configurable via
    // MangaDexIndexerSettings.RateSeconds) is shared across all callers. NO new
    // "mangadex-importlist" bucket — this is the audit gate from Plan 27-05 close-out.
    //
    // T-V7 token-leak prevention: the proxy NEVER logs token values. _logger.Warn(...)
    // calls below carry only the exception message + endpoint path. The single
    // _logger.Trace("Refreshing Token") string (Trakt.cs:137 canonical) is the only
    // refresh-related log emission — emitted from the provider class, not here.
    public interface IMangaDexImportListProxy
    {
        // D-08: POST grant_type=password with user-supplied credentials. Returns the
        // canonical Keycloak token response; provider class persists fields onto
        // Settings POCO.
        MangaDexTokenResponse PasswordGrant(MangaDexImportListSettings settings);

        // D-08 / Trakt.cs:135-163 canonical refresh path. POST grant_type=refresh_token
        // — Keycloak's refresh-token rotation MAY return a new refresh_token (caller
        // applies the Trakt.cs:151 null-coalesce: `Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken`).
        MangaDexTokenResponse RefreshAccessToken(MangaDexImportListSettings settings);

        // Paginated read of /user/follows/manga. Caller (request generator) walks the
        // chain offset+=100 up to MaxNumResultsPerQuery (1000). Returns the raw
        // envelope; parser projects to ImportListItemInfo.
        HttpResponse<MangaDexFollowsResource> GetFollows(MangaDexImportListSettings settings, int offset, int limit);
    }

    public class MangaDexImportListProxy : IMangaDexImportListProxy
    {
        // Verified endpoints (CONTEXT line 273-276 + RESEARCH §STACK §Surface 2):
        private const string TokenUrl = "https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token";
        private const string ApiBaseUrl = "https://api.mangadex.org";

        // Pitfall 10 HARD RULE — shared bucket with MangaDexMetadataSource +
        // MangaDexIndexer + in-process downloader. The string literal MUST match
        // MangaDexIndexer.DefaultSourceKey and MangaDexIndexerSettings.SourceKey
        // default ("mangadex" — verified MangaDexIndexer.cs:53 + MangaDexIndexerSettings.cs:48).
        private const string SharedSourceKey = "mangadex";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public MangaDexImportListProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public MangaDexTokenResponse PasswordGrant(MangaDexImportListSettings settings)
        {
            // OAuth2 password grant per https://api.mangadex.org/docs/02-authentication/personal-clients/
            // and Keycloak OIDC token endpoint contract. Form-urlencoded body shape:
            //   grant_type=password&username={u}&password={p}&client_id={ci}&client_secret={cs}
            // Optional `scope=openid` per Keycloak default; MangaDex personal-client tier
            // does not require additional scopes for /user/follows/manga.
            var request = new HttpRequestBuilder(TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "password")
                .AddFormParameter("username", settings.Username)
                .AddFormParameter("password", settings.Password)
                .AddFormParameter("client_id", settings.ClientId)
                .AddFormParameter("client_secret", settings.ClientSecret)
                .Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public MangaDexTokenResponse RefreshAccessToken(MangaDexImportListSettings settings)
        {
            // OAuth2 refresh-token grant. Form-urlencoded body shape:
            //   grant_type=refresh_token&refresh_token={rt}&client_id={ci}&client_secret={cs}
            var request = new HttpRequestBuilder(TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "refresh_token")
                .AddFormParameter("refresh_token", settings.RefreshToken)
                .AddFormParameter("client_id", settings.ClientId)
                .AddFormParameter("client_secret", settings.ClientSecret)
                .Build();

            ApplySharedHeaders(request);
            return ExecuteTokenRequest(request);
        }

        public HttpResponse<MangaDexFollowsResource> GetFollows(MangaDexImportListSettings settings, int offset, int limit)
        {
            // /user/follows/manga is paginated; the request generator orchestrates the
            // page walk. This method is the per-page HTTP touchpoint and is provided
            // for callers (e.g., the contract Test() path) that need a single direct
            // probe outside the generator's chain.
            var request = new HttpRequestBuilder($"{ApiBaseUrl}/user/follows/manga")
                .AddQueryParam("limit", limit)
                .AddQueryParam("offset", offset)
                .Build();

            ApplySharedHeaders(request);
            request.Headers["Authorization"] = $"Bearer {settings.AccessToken}";

            // T-V7: log ONLY the endpoint + offset/limit — NEVER the bearer-token value.
            _logger.Debug("MangaDex follows: GET /user/follows/manga?limit={0}&offset={1}", limit, offset);
            return _httpClient.Get<MangaDexFollowsResource>(request);
        }

        // Pitfall 10 + Phase 1 D-13: every outbound request from this proxy carries
        //   * RateLimitKey="mangadex"           — shared budget per HARD RULE
        //   * User-Agent="Mangarr/{version}"    — honest UA per MangaDex ToS
        //   * Accept="application/json"         — Keycloak + /user/follows both speak JSON
        // Keep this helper as the SINGLE write-point so the SourceKey audit gate at
        // Plan 27-05 close-out (Pattern κ grep) catches any drift.
        private static void ApplySharedHeaders(HttpRequest request)
        {
            request.RateLimitKey = SharedSourceKey;
            request.Headers["User-Agent"] = $"Mangarr/{BuildInfo.Version.ToString(2)}";
            request.Headers["Accept"] = "application/json";
        }

        private MangaDexTokenResponse ExecuteTokenRequest(HttpRequest request)
        {
            try
            {
                var response = _httpClient.Post<MangaDexTokenResponse>(request);
                return response?.Resource;
            }
            catch (HttpException ex)
            {
                // T-V7: pass message-only, NOT the exception object. NLog's exception
                // formatter calls HttpException.ToString() which serializes the response
                // body / headers — that body can contain credentials echoed in the
                // upstream error payload. Message-only keeps the diagnostic value
                // (endpoint path + HTTP status + error message) without the leak surface.
                _logger.Warn("Error exchanging MangaDex OAuth grant: HTTP {0} {1}",
                    (int?)ex.Response?.StatusCode ?? 0,
                    ex.Message);
                throw;
            }
        }
    }
}
