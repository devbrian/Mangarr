using System;

namespace NzbDrone.Core.Indexers.Cloudflare
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 introduces a generic Cloudflare
    // clearance seam (D-03 / D-09) — a separate axis from the comix.to env-module
    // RESPONSE-SIGNING layer (which stays Comix-only per the Phase 17 IComixSigner
    // "manga-only, don't generalize" rule). CF clearance (passing the challenge →
    // cf_clearance cookie + matched User-Agent) IS allowed to generalize because
    // browser-less aggregator indexers (MangaFire/MangaPark/etc.) will reuse it.

    /// <summary>
    /// Immutable value record carrying one atomic Cloudflare clearance result: the
    /// <c>cf_clearance</c> cookie value paired with the EXACT User-Agent that solved the
    /// challenge. The cookie and UA MUST travel together (Pitfall 4 — a cf_clearance
    /// cookie is bound to the UA that earned it; mixing a cookie with a different UA gets
    /// re-challenged). The solver cookie's <c>domain</c>/<c>path</c>/<c>secure</c>/
    /// <c>httpOnly</c> attributes are copied verbatim (Pitfall 5) so a downstream consumer
    /// (Plan 02 — Comix page injection) can re-apply them faithfully.
    /// </summary>
    public sealed class CloudflareClearance
    {
        public CloudflareClearance(
            string cfClearanceCookie,
            string userAgent,
            string cookieDomain,
            string cookiePath,
            bool secure,
            bool httpOnly,
            DateTimeOffset expiresAt)
        {
            CfClearanceCookie = cfClearanceCookie;
            UserAgent = userAgent;
            CookieDomain = cookieDomain;
            CookiePath = cookiePath;
            Secure = secure;
            HttpOnly = httpOnly;
            ExpiresAt = expiresAt;
        }

        /// <summary>
        /// Construct from a solver cookie + the matched UA + the cache TTL deadline.
        /// Copies the solver cookie's exact <c>domain</c>/<c>path</c>/<c>secure</c>/
        /// <c>httpOnly</c> (Pitfall 5). Missing booleans default false (FlareSolverr omits
        /// the field when false on some builds).
        /// </summary>
        public CloudflareClearance(FlareSolverrCookie cookie, string userAgent, DateTimeOffset expiresAt)
            : this(
                cookie?.Value,
                userAgent,
                cookie?.Domain,
                cookie?.Path,
                cookie?.Secure ?? false,
                cookie?.HttpOnly ?? false,
                expiresAt)
        {
        }

        public string CfClearanceCookie { get; }
        public string UserAgent { get; }
        public string CookieDomain { get; }
        public string CookiePath { get; }
        public bool Secure { get; }
        public bool HttpOnly { get; }
        public DateTimeOffset ExpiresAt { get; }
    }
}
