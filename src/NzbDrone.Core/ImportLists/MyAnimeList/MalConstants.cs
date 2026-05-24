using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 1 — MAL OAuth2 + API endpoint constants.
    //
    // Decision (D-09 per-user model): MAL ClientId is user-supplied via
    // MalImportListSettings.ClientId (NOT compiled-in). Users register their own
    // OAuth client at https://myanimelist.net/apiconfig with redirect URI EXACTLY
    // matching MalConstants.RedirectUri below. Mirrors the per-user client model
    // used by MangaDex + AniList in this phase — Mangarr does NOT register and ship
    // its own upstream MAL OAuth client; each Mangarr install owns its client_id.
    //
    // D-09 (paste-the-callback-URL UX): the RedirectUri below is INTENTIONALLY a URL
    // that Mangarr does NOT serve. MAL redirects the browser to it after the user grants
    // consent; the user copies the FULL URL (containing `?code=X&state=Y`) into the
    // MalCallbackUrlModal in Settings. Zero /callback HTTP listener on Mangarr's side —
    // works behind any reverse proxy / non-default port / containerized deployment.
    //
    // D-01 / V8 ACCEPT: ClientId stored on Settings POCO is NOT a secret per OAuth2
    // public-client semantics (PKCE replaces the shared-secret defense — there is no
    // client_secret for MAL public clients). The secret material is the user's
    // AccessToken+RefreshToken which lives on the Settings POCO and is gated by the
    // Hidden = HiddenType.Hidden flag (Sonarr-canonical TraktSettings pattern).
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

        // D-09 paste-the-callback-URL UX — MUST match the redirect_uri the user registered
        // at https://myanimelist.net/apiconfig for their OAuth client. The browser is
        // redirected here after MAL consent; Mangarr does NOT serve this URL (paste-URL
        // UX intentionally renders the destination unreachable so it works behind any
        // reverse proxy / non-default port without per-deployment configuration). Users
        // MUST configure their MAL OAuth client with this exact redirect URI.
        public const string RedirectUri = "https://mangarr.local/oauth/mal/callback";

        // PKCE verifier byte length — RFC 7636 §4.1 requires 43-128 base64url-encoded
        // characters. 48 raw bytes yields a 64-char base64url-encoded verifier, well
        // within MAL's published 48-128 char acceptance window.
        public const int VerifierByteLength = 48;

        // State nonce byte length — 32 raw bytes yields a ~43-char base64url-encoded
        // nonce; cryptographically random per ASVS V6 (CSPRNG-sourced via
        // System.Security.Cryptography.RandomNumberGenerator).
        public const int StateNonceByteLength = 32;

        // CSRF + replay defense — PKCE state nonce lifetime. Mangarr clears
        // Settings.PendingPkceState on first successful exchange (single-use enforcement)
        // AND on TTL expiry (defense-in-depth). 10 minutes is generous for the user to
        // paste the callback URL but tight enough to limit the window for replay if the
        // browser history leaks the state value.
        public static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

        // Phase 31 IN-01 (GH #258) — pre-baked honest User-Agent literal so call sites
        // stay static-ish (no BuildInfo reflection per request). Phase 1 D-13 honest UA
        // mandatory. Single source-of-truth replaces the prior inline string at
        // MalImportList.FetchPage:446 and the duplicated static field on
        // MalImportListRequestGenerator + MalImportListProxy.
        //
        // Format MUST match the literal shape used across all MAL call sites byte-for-byte
        // (`Mangarr/{major}.{minor}`) — verified against MalImportListProxy.cs:284 and
        // MalImportListRequestGenerator.cs:46 prior to consolidation.
        public static readonly string HonestUserAgent = $"Mangarr/{BuildInfo.Version.ToString(2)}";

        // T-V13 cursor-host validation: MAL's paging.next is returned by the upstream
        // server, but we never blindly trust it for an authenticated request — the
        // Authorization header carries the user's Bearer token, so following a cursor
        // to an attacker-controlled host would leak the token. Pin to the canonical
        // MAL API host AND require an HTTPS scheme.
        //
        // Phase 31 IN-02 (GH #259) — extracted from the byte-for-byte duplication
        // between MalImportList.IsTrustedMalCursor and MalImportListProxy.IsTrustedMalCursor.
        // If one copy is hardened in the future (e.g., IDN host check or path-prefix
        // allow-list) and the other isn't, the divergence becomes a real security hole.
        // Single source-of-truth eliminates that risk surface.
        public static bool IsTrustedMalCursor(string cursor)
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
    }
}
