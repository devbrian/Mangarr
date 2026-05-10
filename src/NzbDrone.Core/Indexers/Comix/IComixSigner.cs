using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Indexers.Comix
{
    // Sonarr divergence: no Sonarr peer. Phase 17 introduces a runtime JS-execution-context
    // signer for comix.to (replaces the static ComixHash port broken 2026-05-10 by upstream
    // key rotation + response-body encryption — see .planning/debug/comix-invalid-token-403.md).
    // Mangarr-only seam; Pattern S2 / sonarr-consistency-audit Pattern ι allowlist coverage.

    /// <summary>
    /// Process-singleton runtime signer for comix.to. Mirrors keiyoushi
    /// <c>Signer.kt</c> (~419 lines, Apache-2.0). Owns the embedded headless Chromium
    /// lifecycle (lazy-spawn, warm page, idle-teardown after 10 min, clean shutdown via
    /// <see cref="NzbDrone.Core.Lifecycle.ApplicationShutdownRequested"/>); concrete impl
    /// is registered <see cref="DryIoc.Reuse"/>.<see cref="DryIoc.Reuse.Singleton"/> via
    /// the existing <c>RegisterMany</c> auto-discovery convention
    /// (<c>NzbDrone.Common/Composition/Extensions.cs:29-31</c>).
    /// </summary>
    public interface IComixSigner
    {
        /// <summary>
        /// Sign and proxyFetch the given comix.to API path through the warm Chromium
        /// page; returns the decoded JSON body (response-interceptor decryption applied
        /// inside the page context). <paramref name="apiPath"/> is the value passed to
        /// keiyoushi <c>proxyFetch</c> — e.g. <c>"/manga/mr3m0/chapters"</c> or
        /// <c>"/chapters/12345/pages"</c>. Path-only — no protocol scheme, no query
        /// string, no <c>..</c>. Phase 17 D-08: callers are
        /// <c>ComixRequestGenerator.GetSearchRequests</c> (chapter list) AND
        /// <c>ComixIndexer.GetChapterPages</c> (chapter pages manifest); per-image GETs
        /// against <c>cdn.comix.to</c> stay on plain <c>IHttpClient</c> per RESEARCH N-3.
        /// </summary>
        Task<string> ProxyFetchAsync(string apiPath, CancellationToken ct = default);
    }
}
