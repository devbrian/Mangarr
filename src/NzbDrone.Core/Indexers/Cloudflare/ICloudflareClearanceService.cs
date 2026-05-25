using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Indexers.Cloudflare
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 introduces a GENERIC Cloudflare
    // clearance seam (D-03 / D-09). This is a SEPARATE axis from comix.to's env-module
    // response-signing (the Phase 17 IComixSigner "manga-only" rule governs the SIGNING
    // axis only). CF clearance is allowed to generalize — any browser-less aggregator
    // indexer (MangaFire/MangaPark/etc.) can consume this interface, proven structurally
    // by FakeHttpAggregatorClearanceConsumerFixture (references no Comix type).

    /// <summary>
    /// Resolves a Cloudflare <c>cf_clearance</c> cookie + the matched User-Agent for a
    /// target URL by POSTing to a user-configured FlareSolverr/Byparr sidecar. The
    /// (cookie, UA) pair is returned atomically (they MUST travel together — Pitfall 4)
    /// and cached per-host with a 20-minute TTL. Auto-discovered as a
    /// <see cref="DryIoc.Reuse"/>.<see cref="DryIoc.Reuse.Singleton"/> via the existing
    /// <c>RegisterMany</c> convention (<c>NzbDrone.Common/Composition/Extensions.cs:29-31</c>).
    /// </summary>
    public interface ICloudflareClearanceService
    {
        /// <summary>
        /// Return a (possibly cached) clearance for <paramref name="targetUrl"/>'s host.
        /// Throws <see cref="CloudflareSolverNotConfiguredException"/> when no solver URL is
        /// configured, or <see cref="CloudflareSolverException"/> when the solver returns a
        /// non-"ok" status or no <c>cf_clearance</c> cookie.
        /// </summary>
        Task<CloudflareClearance> GetClearanceAsync(string targetUrl, CancellationToken ct);
    }
}
