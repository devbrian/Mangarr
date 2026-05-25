using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Indexers.Cloudflare
{
    // Sonarr divergence: no Sonarr peer. Phase 33.2 — generic CF clearance service
    // (D-03 / D-09). NON-disposable singleton: holds ONLY a process-lifetime static
    // readonly HttpClient (Pitfall 2 / T-33.2-03 — avoids socket exhaustion; copies the
    // ComixPuppeteerSigner._relayHttpClient shape) + a per-host TTL cache. Auto-registered
    // Reuse.Singleton via the RegisterMany interface convention — NO explicit registration,
    // NO eager-resolve in Startup.ConfigureServices.

    /// <summary>
    /// POSTs a target URL to the configured FlareSolverr/Byparr sidecar, parses the
    /// <c>cf_clearance</c> cookie + <c>solution.userAgent</c> as one atomic
    /// <see cref="CloudflareClearance"/>, and caches it per-host with a 20-minute TTL
    /// (under Cloudflare's 30-min default — RESEARCH Pattern 3). NEVER logs the cookie
    /// value or the solver's HTML response (T-33.2-01 / ASVS V7) — host + status + typed
    /// error class only.
    /// </summary>
    // NOT sealed: CloudflareClearanceServiceFixture subclasses to override the
    // PostSolveAsync test seam (canned JSON, no network) and ClearanceTtl (near-zero for
    // TTL-expiry assertions). Production safety rests on DryIoc registering THIS class as
    // the Reuse.Singleton via the interface.
    public class CloudflareClearanceService : ICloudflareClearanceService
    {
        // PR-#244-style shared HttpClient (copies ComixPuppeteerSigner._relayHttpClient):
        // process-lifetime, never `using`, intentionally never disposed. Lifetime ≡ process
        // because the service is a DryIoc Reuse.Singleton.
        private static readonly HttpClient _solverHttpClient = new HttpClient();

        private readonly IConfigService _configService;
        private readonly Logger _logger;

        // Per-host (cookie, UA) cache. ConcurrentDictionary because the singleton is shared
        // across indexer pollers + the Phase 4 in-process downloader.
        private readonly ConcurrentDictionary<string, CloudflareClearance> _cache =
            new ConcurrentDictionary<string, CloudflareClearance>();

        public CloudflareClearanceService(IConfigService configService, Logger logger)
        {
            _configService = configService;
            _logger = logger;
        }

        /// <summary>
        /// 20-minute TTL — under Cloudflare's 30-min cf_clearance default (RESEARCH Pattern
        /// 3). <c>protected virtual</c> so a test subclass (or D-12 LIVE tuning) can
        /// override.
        /// </summary>
        protected virtual TimeSpan ClearanceTtl => TimeSpan.FromMinutes(20);

        public async Task<CloudflareClearance> GetClearanceAsync(string targetUrl, CancellationToken ct)
        {
            var host = new Uri(targetUrl).Host;

            if (_cache.TryGetValue(host, out var entry) && DateTimeOffset.UtcNow < entry.ExpiresAt)
            {
                _logger.Debug("Cloudflare clearance cache HIT for host {0}", host);
                return entry;
            }

            var solverUrl = _configService.CloudflareSolverUrl;
            if (string.IsNullOrWhiteSpace(solverUrl))
            {
                // No POST attempted — distinct typed exception feeds the D-07 Health Check.
                throw new CloudflareSolverNotConfiguredException();
            }

            _logger.Debug("Cloudflare clearance cache MISS for host {0}; solving via sidecar", host);

            var solved = await PostSolveAsync(solverUrl, targetUrl, ct).ConfigureAwait(false);

            if (solved?.Status != "ok")
            {
                // Log host + status + solver message only — never the HTML response body.
                _logger.Warn("Cloudflare solver returned non-ok status for host {0}: {1}", host, solved?.Message);
                throw new CloudflareSolverException(solved?.Message ?? "Cloudflare solver returned a non-ok status");
            }

            var cfCookie = solved.Solution?.Cookies?.FirstOrDefault(c => c.Name == "cf_clearance");
            if (cfCookie == null)
            {
                _logger.Warn("Cloudflare solver returned no cf_clearance cookie for host {0}", host);
                throw new CloudflareSolverException("Cloudflare solver returned no cf_clearance cookie");
            }

            var clearance = new CloudflareClearance(
                cfCookie,
                solved.Solution.UserAgent,
                DateTimeOffset.UtcNow + ClearanceTtl);

            _cache[host] = clearance;

            // Never log the cookie value (T-33.2-01) — host + UA presence only.
            _logger.Debug(
                "Cloudflare clearance resolved for host {0} (UA captured: {1})",
                host,
                !string.IsNullOrEmpty(clearance.UserAgent));

            return clearance;
        }

        /// <summary>
        /// Sidecar POST seam. <c>protected virtual</c> so
        /// <c>CloudflareClearanceServiceFixture</c> can return canned JSON with no network
        /// call. Production POSTs <c>request.get</c> to <c>{solverUrl}/v1</c>.
        /// </summary>
        protected virtual async Task<FlareSolverrResponse> PostSolveAsync(string solverUrl, string targetUrl, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(new FlareSolverrRequest { Url = targetUrl });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _solverHttpClient
                .PostAsync($"{solverUrl.TrimEnd('/')}/v1", content, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<FlareSolverrResponse>(json);
        }
    }
}
