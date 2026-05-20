using System;
using System.Collections.Generic;
using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.ImportLists.MangaDex
{
    // Phase 27 Plan 27-02 Task 3 — MangaDex follows-list ThingiProvider plugin.
    //
    // Extends OAuthAwareImportListBase<MangaDexImportListSettings> (Plan 27-01) to inherit:
    //   * RefreshTokenIfNecessary() template (Trakt.cs:127-133 + D-05 SemaphoreSlim wrap)
    //   * Fetch() pre-call refresh-check (base wraps RefreshTokenIfNecessary + base.Fetch)
    //   * Per-Definition.Id semaphore registry (Pitfall 9 mitigation)
    //
    // Pattern source (composite):
    //   * Registration shape: src/NzbDrone.Core.Test/ImportListTests/Fakes/TestImportList.cs
    //   * OAuth RequestAction surface (D-06 Sonarr-canonical):
    //     .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs:102-125
    //   * Refresh persistence + null-coalesce:
    //     .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs:135-163
    //
    // D-08 internal-only OAuth flow: RequestAction("startOAuth") POSTs grant_type=password
    // with user-supplied credentials directly to MangaDex's Keycloak token endpoint and
    // persists the resulting access+refresh tokens on the Settings POCO. NO external
    // OAuth redirect — the FE "Test & Connect" button runs an inline verify-credentials
    // probe via this RequestAction surface.
    //
    // D-05 reactive 401-retry: Fetch() augments the base ladder with a 401-trap that
    // force-expires Settings.Expires, calls RefreshTokenIfNecessary, and retries
    // base.Fetch() exactly once. The proactive 5-minute lookahead in
    // OAuthAwareImportListBase.RefreshTokenIfNecessary handles the common case; the 401
    // trap is the safety net when MangaDex revokes a token early (rotation, lockout, etc.).
    public class MangaDexImportList : OAuthAwareImportListBase<MangaDexImportListSettings>
    {
        private readonly IMangaDexImportListProxy _proxy;
        private readonly IImportListRepository _importListRepository;

        public MangaDexImportList(
            IMangaDexImportListProxy proxy,
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

        public override string Name => "MangaDex";

        public override ImportListType ListType => ImportListType.MangaDex;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        public override IImportListRequestGenerator GetRequestGenerator()
            => new MangaDexImportListRequestGenerator { Settings = Settings };

        public override IParseImportListResponse GetParser()
            => new MangaDexImportListParser();

        // D-06 Sonarr-canonical OAuth action surface (Trakt.cs:102-125 verbatim shape).
        // D-08 internal-only "Test & Connect" flow — `action == "startOAuth"` POSTs the
        // user-supplied credentials to MangaDex's Keycloak token endpoint via the proxy
        // and persists the resulting AccessToken/RefreshToken/Expires/AuthUser on the
        // Settings POCO.
        //
        // Return shape mirrors Trakt's `new { OauthUrl = ... }` envelope but carries
        // a `success: bool` + `authUser: string` payload — no external URL since this
        // is an internal-only flow. The FE OAuth-button handler reads `success` to
        // decide between Save-and-refresh-form vs surface-error-modal.
        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "startOAuth")
            {
                try
                {
                    var response = _proxy.PasswordGrant(Settings);
                    if (response == null || string.IsNullOrWhiteSpace(response.AccessToken))
                    {
                        return new { success = false, error = "MangaDex returned an empty token response. Verify your client_id / client_secret / username / password." };
                    }

                    Settings.AccessToken = response.AccessToken;
                    Settings.RefreshToken = response.RefreshToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(response.ExpiresIn);

                    // AuthUser falls back to the user-supplied Username — MangaDex's
                    // token endpoint doesn't return a username; a follow-up call to
                    // /user/me could fetch the canonical handle, but storing the
                    // submitted Username is sufficient for the FE display affordance.
                    Settings.AuthUser = Settings.Username;

                    return new
                    {
                        success = true,
                        authUser = Settings.AuthUser,
                        expires = Settings.Expires
                    };
                }
                catch (HttpException ex)
                {
                    // T-V7: do NOT include the token / credentials in the error payload —
                    // only the HTTP status + the exception message. The FE displays the
                    // error to the user; sensitive material must not round-trip.
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

        // D-08 / Trakt.cs:135-163 verbatim shape — refresh-token grant with rotation
        // null-coalesce + persistence via IImportListRepository.UpdateSettings. The
        // base's RefreshTokenIfNecessary template invokes this method inside the
        // per-Definition.Id semaphore (D-05 mitigation of Pitfall 9).
        protected override void RefreshToken()
        {
            _logger.Trace("Refreshing Token");

            try
            {
                var response = _proxy.RefreshAccessToken(Settings);

                if (response != null && !string.IsNullOrWhiteSpace(response.AccessToken))
                {
                    Settings.AccessToken = response.AccessToken;
                    Settings.Expires = DateTime.UtcNow.AddSeconds(response.ExpiresIn);

                    // Trakt.cs:151 verbatim — Keycloak refresh-rotation MAY return a
                    // new refresh_token (current MangaDex policy returns the same token
                    // until rotation is enabled; the null-coalesce future-proofs).
                    Settings.RefreshToken = response.RefreshToken ?? Settings.RefreshToken;

                    if (Definition.Id > 0)
                    {
                        _importListRepository.UpdateSettings((ImportListDefinition)Definition);
                    }
                }
            }
            catch (HttpException ex)
            {
                // Sonarr-canonical (Trakt.cs:161) — log the exception but don't bubble.
                // The next Fetch() call's 401-retry decorator (below) will catch the
                // downstream Unauthorized and either re-refresh or surface to the user.
                _logger.Warn(ex, "Error refreshing MangaDex access token");
            }
        }

        // D-05 reactive 401-retry decorator at the per-request level. The base
        // HttpImportListBase.FetchItems loop catches HttpException and records the
        // failure as a generic warning — it does NOT bubble the exception out of
        // Fetch(), so wrapping base.Fetch() in a try/catch around HttpException
        // CANNOT see the 401 (the base eats it first). The correct interception point
        // is FetchImportListResponse — called once per request by base.FetchPage and
        // surrounded by NO exception ladder yet (the ladder lives in FetchItems
        // outside the per-page loop). MAL's MalImportList:283-307 has the canonical
        // shape; this matches it verbatim.
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
                _logger.Debug("MangaDex follows-list returned 401; force-refreshing token and retrying once.");
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
