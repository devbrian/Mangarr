using System;
using System.Linq;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 1 — compiled-in MAL OAuth2 endpoint + ClientId constants.
    //
    // Pattern source (compiled-in ClientId precedent):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/TraktProxy.cs:22-26
    //   (Sonarr-Trakt pins its OAuth ClientId as a compiled-in const string).
    //
    // Decision (D-09 + Pattern D + RESEARCH §Open Question 4): MAL ClientId is
    // Mangarr-pinned, NOT user-supplied. The ClientId identifies the Mangarr app to MAL,
    // not the user (MAL ClientId is NOT a secret in OAuth2 terms; MAL public-client
    // PKCE flow does not use a client secret at all). Mangarr's executor registers a MAL
    // OAuth application at https://myanimelist.net/apiconfig with redirect URI EXACTLY
    // matching the RedirectUri constant below; the issued client_id is then pinned here.
    //
    // TODO (executor-time): ClientId below MUST be replaced with the actual MAL-issued
    // client_id string before shipping a release build. Until then, the placeholder
    // surfaces a 401 (invalid_client) on the first OAuth attempt — by design, so dev
    // builds cannot accidentally make wrong API calls under a real user's MAL account.
    //
    // D-09 (paste-the-callback-URL UX): the RedirectUri below is INTENTIONALLY a URL
    // that Mangarr does NOT serve. MAL redirects the browser to it after the user grants
    // consent; the user copies the FULL URL (containing `?code=X&state=Y`) into the
    // MalCallbackUrlModal in Settings. Zero /callback HTTP listener on Mangarr's side —
    // works behind any reverse proxy / non-default port / containerized deployment.
    //
    // D-01 / V8 ACCEPT: ClientId is plaintext compiled-in (it's NOT a secret per OAuth2
    // public-client semantics — the secret material is the user's AccessToken+RefreshToken
    // which lives on the Settings POCO).
    internal static class MalConstants
    {
        // MAL OAuth2 endpoints — verified against
        // https://myanimelist.net/apiconfig/references/authorization and
        // .planning/phases/27-3-importlist-provider-plugins-v1-1-inserted-2026-05-17/27-CONTEXT.md line 283-286.
        public const string AuthorizeUrl = "https://myanimelist.net/v1/oauth2/authorize";

        public const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";

        // MAL API v2 — Bearer-token-authenticated. /v2/users/@me/mangalist returns the
        // authenticated user's list filtered by Status; supports limit/offset pagination
        // via the `paging.next` cursor URL (NOT explicit page-walk).
        public const string ApiBaseUrl = "https://api.myanimelist.net/v2";

        // D-09 paste-the-callback-URL UX — MUST match the redirect_uri registered with
        // MAL at https://myanimelist.net/apiconfig. The browser is redirected here after
        // MAL consent; Mangarr does NOT serve this URL (paste-URL UX intentionally renders
        // the destination unreachable so it works behind any reverse proxy / non-default
        // port without per-deployment configuration).
        public const string RedirectUri = "https://mangarr.local/oauth/mal/callback";

        // PKCE verifier byte length — RFC 7636 §4.1 requires 43-128 base64url-encoded
        // characters. 48 raw bytes yields a 64-char base64url-encoded verifier, well
        // within MAL's published 48-128 char acceptance window.
        public const int VerifierByteLength = 48;

        // State nonce byte length — 32 raw bytes yields a ~43-char base64url-encoded
        // nonce; cryptographically random per ASVS V6 (CSPRNG-sourced via
        // System.Security.Cryptography.RandomNumberGenerator).
        public const int StateNonceByteLength = 32;

        // Mangarr-pinned compiled-in MAL OAuth ClientId. Executor MUST replace the
        // placeholder below with the actual MAL-issued client_id at release-build time
        // (registered at https://myanimelist.net/apiconfig with redirect URI matching
        // RedirectUri above). Until replaced, MAL returns 401 invalid_client on the
        // first OAuth attempt — by design, so dev builds cannot accidentally exchange
        // codes under a real user's MAL account.
        //
        // ClientId is NOT a secret per OAuth2 public-client semantics (PKCE replaces the
        // shared-secret defense). It identifies the app, not the user; documented in
        // CLAUDE.md "MAL OAuth-app Registration" section.
        public const string ClientId = "REPLACE_AT_RELEASE_BUILD_WITH_MAL_PINNED_CLIENT_ID";

        // CR-02 release-readiness guard (Phase 27 code review). The ClientId constant
        // above is a placeholder until the executor registers a MAL OAuth app at
        // https://myanimelist.net/apiconfig and pins the issued client_id. Without
        // this guard, a forgotten swap silently ships a binary that 401's on every
        // user OAuth attempt with no signal at build or startup time. The placeholder
        // sentinel below is matched verbatim against ClientId — keep them in sync.
        internal const string ClientIdPlaceholderSentinel = "REPLACE_AT_RELEASE_BUILD_WITH_MAL_PINNED_CLIENT_ID";

        internal static bool IsClientIdPlaceholder
            => string.Equals(ClientId, ClientIdPlaceholderSentinel, StringComparison.Ordinal);

        // Detect NUnit at runtime so unit-test fixtures (which stub the proxy and never
        // actually contact MAL) are exempt from the placeholder guard. Production
        // deployments don't load nunit.framework — the guard fires there as intended.
        internal static bool IsTestEnvironment
            => AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) == true);

        // Called from MalImportList.RequestAction (user-facing OAuth surface) to fail
        // fast if a release build ships with the placeholder still in place. Thrown
        // InvalidOperationException surfaces as a visible Settings-form error rather
        // than MAL's silent invalid_client 401 trap.
        internal static void EnsureProductionReady()
        {
            if (IsTestEnvironment)
            {
                return;
            }

            if (IsClientIdPlaceholder)
            {
                throw new InvalidOperationException(
                    "MalConstants.ClientId is set to the release-build placeholder. " +
                    "Replace with the MAL-issued client_id from https://myanimelist.net/apiconfig " +
                    "(redirect URI must match MalConstants.RedirectUri). See Phase 27 code review CR-02.");
            }
        }

        // CSRF + replay defense — PKCE state nonce lifetime. Mangarr clears
        // Settings.PendingPkceState on first successful exchange (single-use enforcement)
        // AND on TTL expiry (defense-in-depth). 10 minutes is generous for the user to
        // paste the callback URL but tight enough to limit the window for replay if the
        // browser history leaks the state value.
        public static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    }
}
