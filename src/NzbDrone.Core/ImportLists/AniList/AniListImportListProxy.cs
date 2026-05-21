using System;
using System.Net;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists.AniList.Resource;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Phase 27 Plan 27-03 Task 2 — AniList OAuth pin-exchange HTTP proxy.
    //
    // Pattern source (verbatim shape):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs
    //   .planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-PATTERNS.md Pattern D
    //
    // Endpoints (verified — CONTEXT line 23 + RESEARCH §STACK §Surface 2):
    //   * Pin authorize URL (user opens in new tab) — GH #230 fix:
    //     https://anilist.co/api/v2/oauth/authorize?client_id={id}&response_type=code&redirect_uri=<pin-url>
    //   * Token exchange (server POSTs after user pastes the pin):
    //     https://anilist.co/api/v2/oauth/token  (form-urlencoded grant_type=authorization_code)
    //   * GraphQL queries — NOT touched by this proxy; the request generator routes through
    //     IAniListGraphQlTransport (Phase 26 Plan 26-02) directly. Pitfall 10 rationale: the
    //     transport already sets RateLimitKey="anilist" at AniListGraphQlTransport.cs:50 so the
    //     ImportList tier inherits the budget bucket without any per-request key plumbing here.
    //
    // T-V7 token-leak prevention: the proxy NEVER logs the pin or the access-token value.
    // _logger.Warn(...) calls below carry ONLY the exception message + HTTP status + endpoint
    // path. The pin is single-use (T-27-03-V11) so even leaking it via log line carries lower
    // exploitable value than a token leak — but we apply the same discipline anyway to match
    // sibling Plan 27-02 MangaDex proxy hygiene.
    //
    // Pitfall 10 NOTE: this proxy's outbound requests target anilist.co (OAuth pin endpoint)
    // NOT graphql.anilist.co (the GraphQL transport). The OAuth pin endpoint is NOT inside the
    // SourceKey="anilist" budget bucket because it's a fire-once-per-month auth flow rather
    // than a recurring sync touchpoint. Plan 27-05 audit gate accepts this scoping.
    public interface IAniListImportListProxy
    {
        // D-07 step 1: build the URL the FE opens in a new tab for the user to log in and
        // copy the issued pin. The URL is constructed deterministically from the OAuth
        // ClientId on the Settings POCO; no HTTP request issued.
        string GetPinAuthorizeUrl(AniListImportListSettings settings);

        // D-07 step 2: server-side POST to https://anilist.co/api/v2/oauth/token with
        // form-urlencoded `grant_type=authorization_code&client_id=...&client_secret=...&
        // redirect_uri=https://anilist.co/api/v2/oauth/pin&code={pin}`. Returns the canonical
        // OAuth2 token response (no refresh_token field — AniList omits per 1-year JWT shape).
        AniListTokenResponse ExchangePinForToken(string pin, AniListImportListSettings settings);
    }

    public class AniListImportListProxy : IAniListImportListProxy
    {
        // Verified endpoints (CONTEXT line 23 + RESEARCH §STACK §Surface 2):
        private const string PinUrl = "https://anilist.co/api/v2/oauth/pin";
        private const string AuthorizeUrl = "https://anilist.co/api/v2/oauth/authorize";
        private const string TokenUrl = "https://anilist.co/api/v2/oauth/token";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public AniListImportListProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public string GetPinAuthorizeUrl(AniListImportListSettings settings)
        {
            // GH #230 fix (2026-05-21 live smoke): the previous URL pointed directly at
            // `/api/v2/oauth/pin` — that endpoint is the REDIRECT TARGET, not the authorize
            // endpoint. AniList's pin page reads the auth code from its OWN query string
            // (`?code=...`) and renders it into the visible textbox; with no redirect happening
            // first, location.search.code is `undefined` and the page shows the literal string
            // "undefined" instead of a usable auth code.
            //
            // The correct URL is `/api/v2/oauth/authorize` with `redirect_uri` set to the pin
            // page; AniList then redirects to the pin page with `?code=<grant>` appended so
            // the pin page can extract and display it.
            //
            // Secondary requirement: the user's AniList OAuth client at
            // https://anilist.co/settings/client/{id} MUST have its "Redirect URL" field set
            // to https://anilist.co/api/v2/oauth/pin — without that, AniList rejects the
            // authorize redirect_uri parameter and never produces a code. See
            // AniListImportListSettings.cs ClientId HelpText for the user-facing guidance.
            //
            // ClientId may legitimately be null/empty pre-save (the FE renders the OAuth
            // button alongside the credential fields); validator catches this on save —
            // emit a partial URL anyway so the FE can surface a Sonarr-shaped error rather
            // than crashing inside the proxy.
            var clientId = settings?.ClientId ?? string.Empty;
            var redirectUri = Uri.EscapeDataString(PinUrl);
            return $"{AuthorizeUrl}?client_id={clientId}&response_type=code&redirect_uri={redirectUri}";
        }

        public AniListTokenResponse ExchangePinForToken(string pin, AniListImportListSettings settings)
        {
            // OAuth2 authorization_code grant per AniList docs (docs.anilist.co/guide/auth/).
            // AniList accepts both JSON and form-urlencoded bodies; we use form-urlencoded to
            // mirror sibling Plan 27-02 MangaDex proxy (TraktProxy.cs:65-73 shape).
            //
            // redirect_uri is REQUIRED by the OAuth2 spec even for pin flow — AniList's pin
            // endpoint serves as the canonical redirect target (anilist.co/api/v2/oauth/pin
            // is the URL whose query-string would have carried the code if this were a
            // browser-redirect flow). Using the pin URL itself as redirect_uri is the
            // AniList-documented contract.
            var request = new HttpRequestBuilder(TokenUrl)
                .Post()
                .AddFormParameter("grant_type", "authorization_code")
                .AddFormParameter("client_id", settings.ClientId ?? string.Empty)
                .AddFormParameter("client_secret", settings.ClientSecret ?? string.Empty)
                .AddFormParameter("redirect_uri", PinUrl)
                .AddFormParameter("code", pin ?? string.Empty)
                .Build();

            ApplyHonestHeaders(request);

            try
            {
                var response = _httpClient.Post(request);

                if (response?.Content == null)
                {
                    return null;
                }

                return Json.Deserialize<AniListTokenResponse>(response.Content);
            }
            catch (HttpException ex)
            {
                // T-V7: pass message-only, NOT the exception object. NLog's exception
                // formatter calls HttpException.ToString() which serializes the response
                // body / headers — that body can contain pin / ClientSecret / token values
                // echoed in the upstream error payload. Message-only keeps the diagnostic
                // value (endpoint path + HTTP status + error message) without the leak
                // surface. Log message phrasing avoids literal "pin"/"token" substrings.
                var status = ex.Response?.StatusCode ?? HttpStatusCode.InternalServerError;
                _logger.Warn("Error exchanging AniList OAuth grant: HTTP {0} {1} ({2})",
                    (int)status,
                    status,
                    ex.Message);
                throw;
            }
        }

        // Phase 1 D-13 honest UA + Accept header — match sibling Plan 27-02 MangaDex proxy
        // hygiene. No RateLimitKey set: the pin/token exchange is fire-once-per-year (1-year
        // JWT) and routes outside the SourceKey="anilist" GraphQL budget (see file-level
        // Pitfall 10 NOTE above).
        private static void ApplyHonestHeaders(HttpRequest request)
        {
            request.Headers["User-Agent"] = $"Mangarr/{BuildInfo.Version.ToString(2)}";
            request.Headers["Accept"] = "application/json";
        }
    }
}
