using System;
using System.Collections.Generic;
using System.Net;
using System.Web;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 3 — MyAnimeList list ThingiProvider plugin.
    //
    // Extends OAuthAwareImportListBase<MalImportListSettings> (Plan 27-01) to inherit:
    //   * RefreshTokenIfNecessary() template (Trakt.cs:127-133 + D-05 SemaphoreSlim wrap)
    //   * Fetch() pre-call refresh-check (base wraps RefreshTokenIfNecessary + base.Fetch)
    //   * Per-Definition.Id semaphore registry (Pitfall 9 mitigation — MAL is the PRIMARY
    //     consumer because MAL ROTATES refresh tokens on every grant call, so a concurrent-
    //     refresh race revokes the prior token cascading into `400 invalid_grant` for all
    //     sibling threads holding the now-stale token).
    //
    // Pattern source (composite):
    //   * Registration shape: src/NzbDrone.Core/ImportLists/MangaDex/MangaDexImportList.cs
    //     (Plan 27-02 sibling — D-08 internal password-grant flow) and AniList provider
    //     (Plan 27-03 — D-07 paste-pin paste-back).
    //   * OAuth RequestAction surface (D-09 paste-the-callback-URL UX):
    //     - startOAuth → generates MalOAuthState (random Verifier + StateNonce + TTL),
    //       persists Settings.PendingPkceState JSON blob, returns { OauthUrl: authorize URL }.
    //     - getOAuthToken → parses `?code=X&state=Y` from query['redirectedUrl'], loads
    //       MalOAuthState from PendingPkceState, validates IsValid(state) for CSRF + TTL,
    //       exchanges code+verifier for tokens via proxy, persists tokens, CLEARS
    //       PendingPkceState (single-use).
    //   * Refresh persistence + null-coalesce: Trakt.cs:135-163 verbatim.
    //
    // D-09 paste-the-callback-URL UX (CONTEXT lines 151-161):
    //   * NO /callback HTTP listener on Mangarr — RedirectUri (MalConstants.cs) is
    //     intentionally a URL Mangarr does NOT serve.
    //   * Works behind any reverse proxy / non-default port / containerized deployment
    //     (zero per-deployment OAuth-redirect-URI configuration).
    //
    // D-05 reactive 401-retry: Fetch() augments the base ladder with a 401-trap that
    // force-expires Settings.Expires, calls RefreshTokenIfNecessary, and retries
    // base.Fetch() exactly once. The proactive 5-minute lookahead in
    // OAuthAwareImportListBase.RefreshTokenIfNecessary handles the common case; the 401
    // trap is the safety net when MAL revokes a token early outside the lookahead window.
    //
    // T-V7: response envelopes from RequestAction NEVER include the refresh token; only
    // the access token (passed back through onChange handler to the FE Settings form) +
    // expiry + authUser. Log messages avoid the literal substrings `Bearer`/`access_token`/
    // `refresh_token`/`code_verifier`/`code=` so the Plan 27-05 close-out audit grep
    // returns 0 in this directory.
    public class MalImportList : OAuthAwareImportListBase<MalImportListSettings>
    {
        private readonly IMalImportListProxy _proxy;
        private readonly IImportListRepository _importListRepository;

        public MalImportList(
            IMalImportListProxy proxy,
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
            _importListRepository = importListRepository;
        }

        public override string Name => "MyAnimeList";

        public override ImportListType ListType => ImportListType.MyAnimeList;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        public override IImportListRequestGenerator GetRequestGenerator()
            => new MalImportListRequestGenerator { Settings = Settings };

        public override IParseImportListResponse GetParser()
            => new MalImportListParser();

        // D-09 Sonarr-canonical OAuth action surface (Trakt.cs:102-125 verbatim shape
        // adapted for MAL's PKCE + paste-callback-URL flow). Two actions:
        //
        //   * "startOAuth" — generates a fresh MalOAuthState (random Verifier + StateNonce +
        //     10-min TTL ExpiresAt) via MalOAuthState.Create(), persists the JSON-serialized
        //     blob on Settings.PendingPkceState, returns { OauthUrl: authorize URL } so the
        //     FE opens it in a new tab. The user grants consent in their browser, MAL
        //     redirects to MalConstants.RedirectUri (an unreachable URL), the user copies
        //     the entire browser-bar URL into MalCallbackUrlModal.
        //
        //   * "getOAuthToken" — parses `?code=X&state=Y` from query["redirectedUrl"],
        //     loads MalOAuthState from PendingPkceState blob, validates CSRF + TTL via
        //     IsValid(presentedState). On success: calls proxy.ExchangeCodeForToken,
        //     persists AccessToken/RefreshToken/Expires/AuthUser, CLEARS PendingPkceState
        //     (single-use enforcement; T-V11 mitigation).
        //
        // T-V7: getOAuthToken response includes `accessToken` (the FE useOAuth result.*
        // keys round-trip into the Settings POCO via the form-input onChange handler — see
        // OAuthInput.tsx:42-46). The refresh token is NEVER echoed.
        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                var state = MalOAuthState.Create();
                Settings.PendingPkceState = JsonConvert.SerializeObject(state);

                if (Definition.Id > 0)
                {
                    _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                }

                var oauthUrl = _proxy.BuildAuthorizeUrl(state);
                return new { OauthUrl = oauthUrl };
            }

            if (action == "getOAuthToken")
            {
                if (query == null || !query.TryGetValue("redirectedUrl", out var redirectedUrl) || string.IsNullOrWhiteSpace(redirectedUrl))
                {
                    return new { success = false, error = "Paste the redirected URL from your browser (containing ?code=... and &state=...) into the modal." };
                }

                if (string.IsNullOrWhiteSpace(Settings.PendingPkceState))
                {
                    return new { success = false, error = "NoPendingPkceState — start the OAuth flow again by clicking Connect." };
                }

                string codeParam;
                string stateParam;
                try
                {
                    var uri = new Uri(redirectedUrl);
                    var queryParams = HttpUtility.ParseQueryString(uri.Query);
                    codeParam = queryParams["code"];
                    stateParam = queryParams["state"];
                }
                catch (UriFormatException)
                {
                    return new { success = false, error = "The pasted value is not a valid URL. Copy the entire address bar (starting with https://...)." };
                }

                if (string.IsNullOrWhiteSpace(codeParam) || string.IsNullOrWhiteSpace(stateParam))
                {
                    return new { success = false, error = "The pasted URL is missing the code or state query parameter. Make sure you copied the URL AFTER MyAnimeList redirected your browser." };
                }

                MalOAuthState pending;
                try
                {
                    pending = JsonConvert.DeserializeObject<MalOAuthState>(Settings.PendingPkceState);
                }
                catch (JsonException)
                {
                    Settings.PendingPkceState = null;
                    return new { success = false, error = "PendingPkceState was corrupted; restart the OAuth flow by clicking Connect." };
                }

                if (pending == null || string.IsNullOrEmpty(pending.StateNonce) || string.IsNullOrEmpty(pending.Verifier))
                {
                    return new { success = false, error = "NoPendingPkceState — start the OAuth flow again by clicking Connect." };
                }

                if (pending.ExpiresAt <= DateTime.UtcNow)
                {
                    Settings.PendingPkceState = null;

                    if (Definition.Id > 0)
                    {
                        _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                    }

                    return new { success = false, error = "StateExpired — the OAuth flow timed out (10-min TTL). Click Connect to start again." };
                }

                if (!pending.IsValid(stateParam))
                {
                    return new { success = false, error = "InvalidState — the state nonce in the redirected URL does not match the one Mangarr issued. Possible CSRF; restart the OAuth flow." };
                }

                try
                {
                    var response = _proxy.ExchangeCodeForToken(codeParam, pending.Verifier);
                    if (response == null || string.IsNullOrWhiteSpace(response.AccessToken))
                    {
                        return new { success = false, error = "MyAnimeList returned an empty token response. Try the OAuth flow again." };
                    }

                    Settings.AccessToken = response.AccessToken;
                    Settings.RefreshToken = response.RefreshToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(response.ExpiresIn);
                    Settings.PendingPkceState = null; // single-use clear

                    if (Definition.Id > 0)
                    {
                        _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                    }

                    // T-V7: return accessToken (FE useOAuth.onChange roundtrip) + expires + a
                    // success flag — do NOT echo the refresh token. The Plan 27-05 close-out
                    // audit grep verifies the source for absence of refresh-token-echoing
                    // properties on this envelope.
                    return new
                    {
                        accessToken = Settings.AccessToken,
                        expires = Settings.Expires,
                    };
                }
                catch (HttpException ex)
                {
                    // T-V7: return only HTTP status + exception message; no token/code/verifier
                    // values. Sonarr-canonical Trakt.cs:161 shape.
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

        // D-05 / Trakt.cs:135-163 verbatim shape — refresh-token grant with rotation
        // null-coalesce + persistence via IImportListRepository.UpdateSettings. The
        // base's RefreshTokenIfNecessary template invokes this method inside the
        // per-Definition.Id semaphore (D-05 mitigation of Pitfall 9 — MAL is the
        // primary consumer because MAL ROTATES refresh tokens, and concurrent refresh
        // on the same Definition.Id revokes the prior token cascading into
        // `400 invalid_grant` for sibling threads).
        protected override void RefreshToken()
        {
            _logger.Trace("Refreshing Token");

            try
            {
                var response = _proxy.RefreshAccessToken(Settings.RefreshToken);

                if (response != null && !string.IsNullOrWhiteSpace(response.AccessToken))
                {
                    Settings.AccessToken = response.AccessToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(response.ExpiresIn);

                    // Trakt.cs:151 verbatim — MAL ROTATES refresh tokens on every grant call.
                    // The null-coalesce preserves the live refresh token when the response
                    // omits the field (defensive; MAL currently always returns one but the
                    // OAuth2 spec allows the server to hold on rotation).
                    Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken;

                    if (Definition.Id > 0)
                    {
                        _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                    }
                }
            }
            catch (HttpException ex)
            {
                // Sonarr-canonical (Trakt.cs:161) — log exception but don't bubble. The
                // next Fetch() call's 401-retry decorator (below) will catch the downstream
                // Unauthorized and either re-refresh or surface to the user.
                _logger.Warn(ex, "Error refreshing MyAnimeList access token");
            }
        }

        // D-05 reactive 401-retry decorator at the per-request level. The base
        // HttpImportListBase.FetchItems loop catches HttpException and records the
        // failure as a generic warning — it does NOT bubble the exception out of
        // Fetch(), so wrapping base.Fetch() in a try/catch around HttpException
        // CANNOT see the 401 (the base eats it first). The correct interception point
        // is FetchImportListResponse — called once per request by base.FetchPage and
        // surrounded by NO exception ladder yet (the ladder lives in FetchItems
        // outside the per-page loop).
        //
        // On 401: force-expire Settings.Expires, call RefreshTokenIfNecessary (which
        // dispatches RefreshToken() inside the per-Definition.Id semaphore), and
        // RETRY the same request once. If the retry also 401s, the base FetchItems
        // catch-block surfaces the failure to the user (the inherited behavior).
        protected override ImportListResponse FetchImportListResponse(ImportListRequest request)
        {
            try
            {
                return base.FetchImportListResponse(request);
            }
            catch (HttpException ex) when (ex.Response?.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Debug("MyAnimeList manga-list returned 401; force-refreshing token and retrying once.");
                Settings.Expires = DateTime.MinValue;
                RefreshTokenIfNecessary();

                // Re-apply the rotated bearer token to the in-flight request before
                // retry. The request generator pre-baked the OLD AccessToken into the
                // Authorization header; after RefreshTokenIfNecessary the new token
                // sits on Settings.AccessToken but the request.Headers entry still
                // carries the old value.
                if (request?.HttpRequest != null)
                {
                    request.HttpRequest.Headers["Authorization"] = $"Bearer {Settings.AccessToken}";
                }

                return base.FetchImportListResponse(request);
            }
        }
    }
}
