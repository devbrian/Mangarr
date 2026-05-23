using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using PuppeteerSharp;
using Timer = System.Timers.Timer;

namespace NzbDrone.Core.Indexers.Comix
{
    // Sonarr divergence: no Sonarr peer. Phase 17 introduces a runtime JS-execution-context
    // signer for comix.to (replaces the static keiyoushi Hash.kt port broken 2026-05-10 by
    // upstream key rotation + response-body encryption — see
    // .planning/debug/comix-invalid-token-403.md).
    // Mangarr-only seam; Pattern S2 / sonarr-consistency-audit Pattern ι allowlist coverage.

    /// <summary>
    /// Process-singleton runtime signer for comix.to. Mirrors keiyoushi <c>Comix.kt</c>
    /// <c>captureToken()</c> (Apache-2.0; upstream commit <c>965dc242</c> 2026-05-12 —
    /// "Comix: only get token via webview"). Owns an embedded headless Chromium child
    /// process (PuppeteerSharp 24.42.0): lazy-spawn warm page, idle-teardown after 10 min
    /// (D-12), clean shutdown via <see cref="ApplicationShutdownRequested"/>.
    /// Auto-registered <see cref="DryIoc.Reuse"/>.<see cref="DryIoc.Reuse.Singleton"/> via
    /// the existing <c>NzbDrone.Common/Composition/Extensions.cs:25-35</c> RegisterMany
    /// convention.
    ///
    /// <para>
    /// <b>Architecture (post-2026-05-22 rotation):</b> comix.to's signer function is no
    /// longer reachable from <c>globalThis.&lt;namespace&gt;.&lt;fn&gt;</c> shape — the
    /// previous behaviour-probe approach (Phase 17 D-14) is upstream-obsolete. The new
    /// shape <b>observes</b> the page's own outgoing API request and extracts the
    /// <c>_=&lt;token&gt;</c> query parameter via PuppeteerSharp request interception. Once
    /// the token is captured, the actual API GET is relayed server-side via a process-
    /// singleton <see cref="System.Net.Http.HttpClient"/> (Choice B per
    /// <c>.planning/debug/comix-signer-rotation.md</c>); PR #244 review feedback refined
    /// this to forward the page's cookies + cached User-Agent onto the relay request so
    /// the captured token reuses its originating session fingerprint.
    /// </para>
    ///
    /// <para>
    /// <b>Token cache:</b> captured tokens are cached per-pageUrl with a 5-minute TTL so
    /// chapter-list pagination loops can reuse a single capture (the upstream Android shape
    /// re-captures per request because WebView spin-up is cheap there; PuppeteerSharp page
    /// loads are 3-8s, worth caching).
    /// </para>
    /// </summary>
    // NOTE: NOT `sealed` — Wave 1 fixtures (FastIdleSigner / ThrowingSigner /
    // ReprobableSigner / GatedSigner per Wave 0 fixture commentary) subclass this type to
    // override the `protected virtual` test seams (IdleTimeout / LaunchBrowserAsync /
    // LaunchAndProbeAsync / EvaluateProxyFetchAsync / ProxyFetchAsyncImpl). Plan 17-02
    // Task 1a body literal said `sealed`; the fixture-elaboration contract directly
    // contradicts it. Per the plan's W-1 split + B-2 path (a) lazy-reprobe test design,
    // the test seams win — and production safety rests on DryIoc registering THIS class
    // as the singleton (verified by ComixSignerDryIocResolutionFixture).
    public class ComixPuppeteerSigner :
        IComixSigner,
        IDisposable,
        IHandle<ApplicationShutdownRequested>
    {
        // ── Constants ────────────────────────────────────────────────────────────────
        // SA1203: const fields must precede static readonly + instance fields.
        private const string ComixSourceKey = "comix.to";
        private const string ComixBaseUrl = "https://comix.to";

        // captureToken time budget (mirrors upstream Comix.kt:466 — `latch.await(30, SECONDS)`).
        // Page DCL hits in ~1-3s on warm cache; the page's own bootstrap fires the
        // /api/v1/manga/{hid}/chapters request 2-5s after that. 30s is comfortably above
        // p99 with margin for Cloudflare slow path. Bounded so a wedged page (no API
        // request ever fires) cannot stall the gate indefinitely.
        private const int CaptureTimeoutSeconds = 30;

        // Token cache TTL — chapter-list pagination + multi-search loops within 5 minutes
        // reuse the captured token instead of paying another page-load cost. Upstream
        // re-captures per request because Android WebView spin-up is cheap; our
        // PuppeteerSharp page loads are 3-8s so caching is a meaningful win.
        private const int TokenCacheTtlMinutes = 5;

        // ── Static readonly fields ───────────────────────────────────────────────────
        // W-2 (revision iteration 1): drain timeout for in-flight requests on Dispose.
        private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

        // PR #244 review feedback (Codex P1 / CodeRabbit Major) — shared HttpClient for the
        // captured-token relay path. The standard .NET guidance is to reuse a single
        // HttpClient across the process to avoid socket exhaustion + DNS pinning issues
        // that a per-call `new HttpClient()` introduces. Process-lifetime ownership is
        // acceptable here because the signer itself is a process singleton (DryIoc
        // `Reuse.Singleton`) so the HttpClient lifetime ≡ process lifetime. Not wrapped
        // in `using`; intentionally never disposed.
        private static readonly System.Net.Http.HttpClient _relayHttpClient =
            new System.Net.Http.HttpClient();

        // V5 input validation per RESEARCH §"Security Domain" / T-17-02-01.
        // Pre-compiled for hot-path performance; constraint: path-only, no scheme/host/..
        private static readonly System.Text.RegularExpressions.Regex ApiPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/[A-Za-z0-9_\\-/]+(\\?[A-Za-z0-9_\\-/=&%.]*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // captureToken apiPath shape parsers — derive (pageUrl, matchPredicate) from the
        // path the caller passes to ProxyFetchAsync. These mirror upstream Comix.kt's call-
        // site routes plus the title→hid resolution endpoint (2026-05-23 cascade fix from
        // Phase 3 env-module-oracle pivot — keyword-search GETs were still going via plain
        // HTTP and getting Cloudflare-403'd; routing them through the signer reuses the
        // bundle's pre-installed axios interceptor chain which handles the CF challenge
        // and the decryption + ok+result unwrap):
        //   /manga/{hid}/chapters[?...]  → load /title/{hid}; match /api/v1/manga/{hid}/chapters
        //   /chapters/{chapterId}[?...]  → load /chapters/{chapterId}; match /api/v1/chapters/{chapterId}
        //   /manga[?keyword=...]         → load /; match /api/v1/manga
        private static readonly System.Text.RegularExpressions.Regex MangaChaptersPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/manga/(?<hid>[A-Za-z0-9_-]+)/chapters(?:\\?.*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex ChaptersDetailPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/chapters/(?<chapterId>[A-Za-z0-9_-]+)(?:\\?.*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex MangaSearchPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/manga(?:\\?.*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // ── Test-overridable seams (D-12 / fixture overrides) ────────────────────────

        /// <summary>
        /// D-12: hardcoded const for v1.0; <c>protected virtual</c> so test fixtures can
        /// override (see <c>ComixSignerIdleTeardownFixture.FastIdleSigner</c> per the
        /// fixture-Wave-1 elaboration shape).
        /// </summary>
        protected virtual TimeSpan IdleTimeout => TimeSpan.FromMinutes(10);

        /// <summary>
        /// Page-context impl seams. Test fixtures override via subclass for FailSoft /
        /// IdleTeardown / LazyReprobe scenarios — overrides do NOT need a real Chromium
        /// child. Production paths in this file fill these with the PuppeteerSharp impl.
        /// </summary>
        protected virtual async Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
        {
            // WR-03 mitigation (revision iteration 2): observe ct before issuing the
            // (synchronously-launching) Puppeteer call. PuppeteerSharp's LaunchAsync
            // does not accept a CancellationToken in 24.42.0; the most we can do is
            // bail before starting and rely on subsequent ct.ThrowIfCancellationRequested()
            // calls between awaits to abort partway through the spawn sequence.
            ct.ThrowIfCancellationRequested();

            // Pitfall 7 (Chromium-as-root in container): launch args required for headless
            // Chrome inside Docker — --no-sandbox + setuid disable + dev/shm fallback +
            // GPU disable. ExecutablePath comes from PUPPETEER_EXECUTABLE_PATH (D-03 escape
            // hatch) or the baked image-layer path resolved by GetBakedChromiumPath (Phase 17
            // D-03 default plus Phase 17.2 follow-up Windows / Mac / Linux non-Docker
            // platform-aware fallbacks — see comment above GetBakedChromiumPath).
            var launchOptions = new LaunchOptions
            {
                Headless = true,
                ExecutablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH")
                                ?? GetBakedChromiumPath(),
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                },
            };

            return await Puppeteer.LaunchAsync(launchOptions).ConfigureAwait(false);
        }

        /// <summary>
        /// Warm-start: launches a Chromium child + opens a single page. The page is reused
        /// across requests; per-call <see cref="EvaluateProxyFetchAsync"/> installs a fresh
        /// request-interception handler that captures the page's outgoing
        /// <c>?_=&lt;token&gt;</c> query parameter on its OWN bootstrap API call.
        ///
        /// <para>
        /// Previously this method evaluated a <c>PROBE_JS</c> namespace walker against
        /// <c>https://comix.to/</c> to capture <c>window.&lt;namespace&gt;.signer</c> +
        /// <c>installer</c> function refs. As of upstream commit <c>965dc242</c>
        /// (2026-05-12), the signer is no longer reachable from globalThis at all — the
        /// captureToken pattern observes the page's own request instead. Probe + the
        /// Phase 17.2 D-1 networkidle-settle step were both retired.
        /// </para>
        /// </summary>
        // Sonarr divergence: Mangarr-only seam (Pattern S2 / sonarr-consistency-audit
        // Pattern ι allowlist coverage). See:
        //   .planning/debug/comix-signer-rotation.md (root cause + captureToken pivot)
        //   .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-LEARNINGS.md
        protected virtual async Task LaunchAndProbeAsync(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // WR-03 mitigation: observe ct between each await. PuppeteerSharp's
                // NewPageAsync doesn't accept ct directly; throw-on-request between calls
                // is the best we get.
                _browser = await LaunchBrowserAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                _page = await _browser.NewPageAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                // PR #244 review feedback (Codex P1 #2): cache navigator.userAgent off the
                // warm page once so per-relay calls don't pay an extra EvaluateExpressionAsync
                // round-trip. The UA is bound to the browser instance (not per-navigation)
                // so caching at warm-time is safe — only invalidated on TeardownBrowserAsync.
                try
                {
                    _cachedUserAgent = await _page
                        .EvaluateExpressionAsync<string>("navigator.userAgent")
                        .ConfigureAwait(false);
                }
                catch (Exception uaEx)
                {
                    // Non-fatal: UA capture is a best-effort forward; relay still works
                    // without it (falls back to .NET's default User-Agent header).
                    _logger.Debug(uaEx, "Comix signer: navigator.userAgent capture raised; relay will use default UA.");
                    _cachedUserAgent = null;
                }

                // Request interception is toggled per-call inside CaptureTokenAsync
                // (NOT warm-attached here) — keeping interception ON across the relay
                // fetch step would block the relay's outgoing /api/v1 GET (each
                // intercepted request requires explicit ContinueAsync/AbortAsync;
                // with no handler attached, the request stalls until PuppeteerSharp's
                // 180s command timeout fires).
                _probeFailureCount = 0;
                sw.Stop();

                _logger.Info(
                    "Comix signer: Chromium launched, page ready for env-module oracle (warm latency: {0}ms).",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _probeFailureCount++;
                _logger.Warn(ex, "Comix signer: probe failed (n={0}); relaunching browser.", _probeFailureCount);
                _sourceStatusService.RecordFailure(ComixSourceKey);
                await TeardownBrowserAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// ENV-MODULE ORACLE flow (2026-05-23 — Investigation Phase 3):
        /// <list type="number">
        ///   <item>Derive <c>pageUrl</c> from <paramref name="apiPath"/> (the bootstrap
        ///         page that loads the bundle into context).</item>
        ///   <item>Navigate to <c>pageUrl</c> (only once per warm page — subsequent
        ///         calls reuse the already-loaded env module without re-navigating).</item>
        ///   <item>Capture the URL of the bundle's <c>env-tfgaak-*.js</c> module while
        ///         the page loads (it imports the secure bundle's <c>Hi</c> decryption
        ///         installer and calls <c>Hi(ai)</c> to add an axios interceptor that
        ///         automatically handles the <c>{e:"..."}</c> envelope).</item>
        ///   <item>From page context, dynamically import the env module and call
        ///         <c>mod.f.get(apiPath, {params})</c>. Returns plaintext JSON via the
        ///         pre-installed decryption + ok+result unwrap interceptor chain.</item>
        /// </list>
        /// <para>
        /// Investigation Phase 3 (2026-05-23) proved this oracle returns plaintext for
        /// both endpoints: <c>/manga/{hid}/chapters</c> → <c>{items:[...], meta:...}</c>
        /// and <c>/chapters/{id}</c> → <c>{id, ..., pages:{baseUrl, items:[...]}, ...}</c>.
        /// </para>
        /// </summary>
        protected virtual async Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
        {
            // CR-05 mitigation (revision iteration 2): snapshot fields under the gate
            // before any await against the page so a concurrent Dispose-after-drain that
            // nulls _page cannot NRE us mid-evaluate.
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            var page = _page;
            if (page == null)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            // Derive bootstrap pageUrl from apiPath. Three call-site shapes:
            //   /manga/{hid}/chapters[?...]   →  https://comix.to/title/{hid}
            //   /chapters/{chapterId}[?...]   →  https://comix.to/chapters/{chapterId}
            //   /manga[?keyword=...]          →  https://comix.to/
            var (pageUrl, _) = ResolveCaptureRoute(apiPath);

            // Ensure the env module URL is captured + cached. On first call this also
            // navigates to pageUrl (bootstraps the bundle). Subsequent calls within
            // BootstrapCacheTtlMinutes reuse the warm page + cached env module URL.
            var envModuleUrl = await EnsureEnvModuleAsync(page, pageUrl, ct).ConfigureAwait(false);

            // Split apiPath into path + query params for axios. The query portion (if
            // any) is converted into a params object for `f.get(path, {params})`.
            var (pathPart, paramsJson) = SplitApiPathToAxiosCall(apiPath);

            // Call mod.f.get(pathPart, {params: paramsObj}) from page context. The 'f'
            // export IS the 'b' wrapper from env-tfgaak: it returns `(await ai.get(...)).data`
            // post both the decryption interceptor (installed by Hi(ai)) and the
            // ok+result unwrap interceptor — so the result is the inner `.result` object
            // already-decrypted.
            //
            // Mirrors upstream Comix.kt's `client.newCall(GET(url, headers))` shape
            // semantically (returns plaintext JSON body), but routes through the bundle's
            // own already-installed decryption interceptor instead of doing a vanilla
            // HTTP relay. Choice B (vanilla HTTP relay) does NOT work post-2026-05-22
            // rotation: comix.to encrypts response bodies REGARDLESS of which HTTP client
            // issues the request, and the decryption oracle lives inside the bundle's
            // closure-scoped VMP-protected runtime (only invokable via the env module's
            // axios instance). See .planning/debug/comix-signer-rotation.md Investigation
            // Phase 3 for full root-cause + decision.
            ct.ThrowIfCancellationRequested();

            // 2026-05-23 cascade fix: paramsJson is a JSON STRING built C#-side (e.g.
            // `{"keyword":"...","limit":"10"}`). PuppeteerSharp's EvaluateFunctionAsync
            // serializes each arg via JSON, so a C# string lands as a JS string — meaning
            // `paramsObj` was previously the literal text `"{}"` (NOT an object). axios
            // tolerated this by accident on the chapter-list path (empty params object
            // construction); the keyword-search path with non-empty params hit
            // `TypeError: target must be an object` because axios's URL composition tried
            // to iterate the string as if it were an object. Fix: parse the JSON inside
            // the page-context wrapper so the axios call receives a real object.
            var resultJson = await page.EvaluateFunctionAsync<string>(
                "async (modUrl, p, paramsJson) => {" +
                "  const mod = await import(modUrl);" +
                "  const f = mod.f;" + // 'b as f' export — wraps ai.get with .data unwrap
                "  const paramsObj = paramsJson ? JSON.parse(paramsJson) : {};" +
                "  const opts = Object.keys(paramsObj).length > 0 ? { params: paramsObj } : undefined;" +
                "  const res = await f.get(p, opts);" +
                "  return typeof res === 'string' ? res : JSON.stringify(res);" +
                "}",
                envModuleUrl,
                pathPart,
                paramsJson).ConfigureAwait(false);

            return resultJson;
        }

        // Bootstrap-cache: env module URL is stable per comix.to build (bundle URL is
        // content-hashed). Cache for 5 minutes so multi-search loops don't re-navigate.
        private string _cachedEnvModuleUrl;
        private DateTimeOffset _envModuleCachedAt;

        private static readonly TimeSpan BootstrapCacheTtl = TimeSpan.FromMinutes(5);

        // EnsureEnvModuleAsync: navigates to pageUrl (only if env module URL not cached),
        // sniffs the env module URL from network traffic, caches it, returns it.
        private async Task<string> EnsureEnvModuleAsync(IPage page, string pageUrl, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(_cachedEnvModuleUrl)
                && DateTimeOffset.UtcNow - _envModuleCachedAt < BootstrapCacheTtl)
            {
                return _cachedEnvModuleUrl;
            }

            var envUrlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<RequestEventArgs> finishedHandler = null;
            finishedHandler = (sender, e) =>
            {
                try
                {
                    var url = e.Request?.Url;
                    if (url != null
                        && url.IndexOf("env-tfgaak-", StringComparison.OrdinalIgnoreCase) >= 0
                        && url.IndexOf("comix.to", StringComparison.OrdinalIgnoreCase) >= 0
                        && url.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        envUrlTcs.TrySetResult(url);
                    }
                }
                catch
                {
                    // swallow
                }
            };

            page.RequestFinished += finishedHandler;
            try
            {
                // Fire-and-forget navigation — the env module capture is our sync point.
                _ = page.GoToAsync(
                    pageUrl,
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delayTask = Task.Delay(TimeSpan.FromSeconds(CaptureTimeoutSeconds), linkedCts.Token);
                var winner = await Task.WhenAny(envUrlTcs.Task, delayTask).ConfigureAwait(false);

                if (winner != envUrlTcs.Task)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(
                        $"Comix signer: env module capture timed out after {CaptureTimeoutSeconds}s while loading '{pageUrl}'.");
                }

                linkedCts.Cancel();
                var envUrl = await envUrlTcs.Task.ConfigureAwait(false);
                _cachedEnvModuleUrl = envUrl;
                _envModuleCachedAt = DateTimeOffset.UtcNow;

                // Give the bundle a brief beat to finish loading the env module (it's the
                // last script in the dependency chain, so once RequestFinished fires the
                // import side-effects — calling Hi(ai) to install the decryption
                // interceptor — should be complete in ms). 500ms is overkill but cheap.
                await Task.Delay(500, ct).ConfigureAwait(false);

                return envUrl;
            }
            finally
            {
                page.RequestFinished -= finishedHandler;
            }
        }

        // SplitApiPathToAxiosCall: turn `/manga/mr3m0/chapters?page=1&limit=20` into
        // (pathPart="/manga/mr3m0/chapters", paramsJson="{\"page\":\"1\",\"limit\":\"20\"}")
        // so it can be passed to `mod.f.get(pathPart, {params: paramsObj})` from page JS.
        private static (string PathPart, string ParamsJson) SplitApiPathToAxiosCall(string apiPath)
        {
            var qIdx = apiPath.IndexOf('?');
            if (qIdx < 0)
            {
                return (apiPath, "{}");
            }

            var pathPart = apiPath[..qIdx];
            var query = apiPath[(qIdx + 1)..];
            var pairs = new List<string>();
            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                {
                    continue;
                }

                var eq = pair.IndexOf('=');
                string key;
                string value;
                if (eq < 0)
                {
                    key = pair;
                    value = string.Empty;
                }
                else
                {
                    key = pair[..eq];
                    value = pair[(eq + 1)..];
                }

                // Decode + re-encode as JSON string.
                var decodedKey = Uri.UnescapeDataString(key);
                var decodedValue = Uri.UnescapeDataString(value);
                var keyJson = System.Text.Json.JsonSerializer.Serialize(decodedKey);
                var valueJson = System.Text.Json.JsonSerializer.Serialize(decodedValue);
                pairs.Add($"{keyJson}:{valueJson}");
            }

            return (pathPart, "{" + string.Join(",", pairs) + "}");
        }

        // Resolve which page URL to load to bootstrap the bundle. Two call-site shapes
        // mirror upstream Comix.kt's `getMangaUrl` / `getChapterUrl` routes — preserved
        // even after the 2026-05-23 oracle pivot because the bootstrap pageUrl must be
        // sensible for the bundle to render normally (so it loads the env module).
        private static (string PageUrl, string MatchSuffix) ResolveCaptureRoute(string apiPath)
        {
            var m = MangaChaptersPathRegex.Match(apiPath);
            if (m.Success)
            {
                var hid = m.Groups["hid"].Value;
                return (
                    $"{ComixBaseUrl}/title/{hid}",
                    $"/api/v1/manga/{hid}/chapters");
            }

            m = ChaptersDetailPathRegex.Match(apiPath);
            if (m.Success)
            {
                var chapterId = m.Groups["chapterId"].Value;
                return (
                    $"{ComixBaseUrl}/chapters/{chapterId}",
                    $"/api/v1/chapters/{chapterId}");
            }

            if (MangaSearchPathRegex.IsMatch(apiPath))
            {
                // 2026-05-23 cascade fix: bootstrap page for the keyword-search endpoint is
                // the comix.to home page — it loads the bundle + env-tfgaak module, exposing
                // the same axios instance that signs+decrypts the chapter-list path. The
                // matchSuffix slot is unused for this route (EnsureEnvModuleAsync filters by
                // env-tfgaak-*.js URL pattern, NOT by API path), so a plain "/api/v1/manga"
                // is fine here.
                return ($"{ComixBaseUrl}/", "/api/v1/manga");
            }

            throw new ArgumentException(
                $"Comix signer: apiPath '{apiPath}' does not match a supported route " +
                "(supported: /manga/{hid}/chapters, /chapters/{chapterId}, or /manga[?keyword=...]).",
                nameof(apiPath));
        }

        // Extract a single query-parameter value WITHOUT pulling in System.Web. The query
        // string passed in starts with '?' (Uri.Query convention). Returns null when the
        // param isn't present.
        private static string ExtractQueryParam(string query, string name)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }

            var trimmed = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            foreach (var pair in trimmed.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                {
                    continue;
                }

                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var key = pair.Substring(0, eq);
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    var value = pair.Substring(eq + 1);
                    return Uri.UnescapeDataString(value);
                }
            }

            return null;
        }

        // ── Token cache ─────────────────────────────────────────────────────────────

        private sealed class CachedToken
        {
            public string Token { get; set; }
            public DateTimeOffset CapturedAt { get; set; }
        }

        // Per-pageUrl cache. ConcurrentDictionary because the gate serializes
        // captureToken work but ProxyFetchAsync entries can read the cache snapshot
        // before acquiring the gate (read-only race-tolerant); writers are gate-serialized.
        private readonly ConcurrentDictionary<string, CachedToken> _tokenCache =
            new ConcurrentDictionary<string, CachedToken>();

        private string TryGetCachedToken(string pageUrl)
        {
            if (!_tokenCache.TryGetValue(pageUrl, out var entry))
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - entry.CapturedAt > TimeSpan.FromMinutes(TokenCacheTtlMinutes))
            {
                _tokenCache.TryRemove(pageUrl, out _);
                return null;
            }

            return entry.Token;
        }

        private void CacheToken(string pageUrl, string token)
        {
            _tokenCache[pageUrl] = new CachedToken
            {
                Token = token,
                CapturedAt = DateTimeOffset.UtcNow,
            };
        }

        // T-17-02-02 mitigation: single-quote string substitution helper. Escapes \ and '
        // before string-format-style insertion into the page-context JS template.
        private static string JsString(string s) =>
            "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        // Phase 17.2 follow-up — platform-aware Chromium cache discovery.
        //
        // Resolution order:
        //   1. PUPPETEER_CACHE_DIR env var (operator override; D-03 escape hatch)
        //   2. Platform-conventional candidates (first one with an installed browser wins):
        //        - /opt/mangarr-chromium               (Linux Docker — D-03 image-layer default)
        //        - $HOME/.cache/mangarr-chromium       (Linux non-Docker, XDG; matches
        //                                                tools/ChromiumPrefetch local-dev path)
        //        - %LOCALAPPDATA%\mangarr-chromium     (Windows convention)
        //        - $HOME/Library/Caches/mangarr-chromium (macOS convention)
        //
        // Phase 17 shipped only the /opt/mangarr-chromium default — it works in the production
        // Docker image but returns null on every other host (Windows / Mac / Linux non-Docker
        // dev), so PuppeteerSharp falls back to looking for chrome relative to the working dir
        // at `_output/net10.0/Chrome/...` and throws ProcessException at first request. This
        // gap was invisible in Phase 17 (the live signer never actually worked end-to-end so
        // no one hit the launcher path); Phase 17.2's Mitigation A re-greened the live fixtures
        // and surfaced it on the very first manual search through the running app.
        //
        // Returns null only when no candidate has an installed browser — Puppeteer.LaunchAsync
        // will then surface its own error (caught + RecordFailure'd by LaunchAndProbeAsync).
        private static string GetBakedChromiumPath()
        {
            var explicitOverride = Environment.GetEnvironmentVariable("PUPPETEER_CACHE_DIR");
            var candidates = explicitOverride != null
                ? new[] { explicitOverride }
                : GetPlatformCacheCandidates();

            foreach (var cacheDir in candidates)
            {
                if (string.IsNullOrEmpty(cacheDir))
                {
                    continue;
                }

                try
                {
                    var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = cacheDir });
                    var installed = System.Linq.Enumerable.FirstOrDefault(fetcher.GetInstalledBrowsers());
                    var execPath = installed?.GetExecutablePath();
                    if (!string.IsNullOrEmpty(execPath))
                    {
                        return execPath;
                    }
                }
                catch
                {
                    // Try next candidate.
                }
            }

            return null;
        }

        // Phase 17.2 follow-up — platform-conventional Chromium cache directories.
        //
        // Order: production Docker default first (preserves Phase 17 D-03 semantics
        // for the production image), then local-dev conventions per OS. Yielded
        // lazily so HOME / LOCALAPPDATA are read on the host that's running.
        // `internal` so the regression guard in
        // ComixSignerPlatformCacheFallbackFixture.Launcher_must_have_platform_aware_cache_fallback
        // can spot-check the candidate list directly.
        internal static System.Collections.Generic.IEnumerable<string> GetPlatformCacheCandidates()
        {
            // Linux Docker default first (production image-layer path; Phase 17 D-03)
            yield return "/opt/mangarr-chromium";

            var home = Environment.GetEnvironmentVariable("HOME")
                       ?? Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(home))
            {
                // XDG cache convention (Linux non-Docker; also matches the path
                // tools/ChromiumPrefetch writes to on the local Windows dev host
                // when invoked with `--output-dir $HOME/.cache/mangarr-chromium`)
                yield return System.IO.Path.Combine(home, ".cache", "mangarr-chromium");
            }

            // Windows %LOCALAPPDATA% (Windows convention; preferred when set)
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return System.IO.Path.Combine(localAppData, "mangarr-chromium");
            }

            // macOS ~/Library/Caches (macOS convention)
            if (!string.IsNullOrEmpty(home))
            {
                yield return System.IO.Path.Combine(home, "Library", "Caches", "mangarr-chromium");
            }
        }

        // ── Fields ────────────────────────────────────────────────────────────────────
        private readonly IIndexerSourceStatusService _sourceStatusService;
        private readonly Logger _logger;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        // WR-08 mitigation (revision iteration 2): `volatile` so cross-thread reads of
        // _disposed in ProxyFetchAsync / EvaluateProxyFetchAsync / OnIdleElapsed observe
        // the Dispose-side write without unbounded delay. Without volatile, the .NET
        // memory model gives atomic bool reads but not visibility guarantees across
        // cores — a Dispose on Thread A may not be observed by an in-flight ProxyFetch
        // on Thread B for an unbounded interval, allowing the lazy-reprobe to spin a
        // new Chromium child after the public Dispose path already executed.
        // (For the CR-04 / CR-05 race-safety logic to be tight, this read must be
        // immediate.)
        private volatile bool _disposed;

        // _probeFailureCount tracks the n-count surfaced in the D-15 probe-failure Warn
        // line. Reset to 0 on a successful warm-spawn; incremented on each spawn throw.
        private int _probeFailureCount;

        // Page-context state — mutated only under _gate.
        // Pitfall 4: nulled BEFORE awaiting browser.CloseAsync inside TeardownBrowserAsync.
        private IBrowser _browser;
        private IPage _page;

        // PR #244 review feedback (Codex P1 #2): cached page User-Agent string snapshot
        // captured at warm-time. Forwarded onto the relay HttpClient request so the
        // captured-token GET reuses the page's session fingerprint (Cloudflare + comix.to
        // session-mismatch hypothesis). Reset to null on TeardownBrowserAsync.
        private string _cachedUserAgent;

        private Timer _idleTimer;

        // WR-06 mitigation (revision iteration 2): generation counter for the idle
        // timer's fire-then-rearm race. System.Timers.Timer.Stop() does NOT pull back
        // an Elapsed event already queued to the ThreadPool, so OnIdleElapsed can
        // execute even after ReArmIdleTimer ran. Each ReArmIdleTimer bumps
        // _idleTimerGeneration; OnIdleElapsed snapshots its own generation at entry
        // and bails (without tearing down) if it has been superseded.
        // Using `int` + Interlocked is fine for a 64-bit-host monotonic counter — the
        // wraparound interval at 1 increment per minute is ~4000 years.
        private int _idleTimerGeneration;

        public ComixPuppeteerSigner(IIndexerSourceStatusService sourceStatusService, Logger logger)
        {
            _sourceStatusService = sourceStatusService;
            _logger = logger;
        }

        // ── Public surface ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sign-and-fetch the given comix.to API path through the warm Chromium page;
        /// returns the decoded JSON body. V5 input validation (T-17-02-01) at the entry
        /// guards the JS-injection surface; the gate / launch / evaluate / reprobe flow
        /// happens inside <see cref="ProxyFetchAsyncImpl"/>.
        /// </summary>
        public Task<string> ProxyFetchAsync(string apiPath, CancellationToken ct = default)
        {
            // T-17-02-01 mitigation: strict allowlist regex BEFORE touching the page.
            if (string.IsNullOrEmpty(apiPath) || !ApiPathRegex.IsMatch(apiPath))
            {
                throw new ArgumentException(
                    $"Comix signer: apiPath must match {ApiPathRegex} — got '{apiPath}'.",
                    nameof(apiPath));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            return ProxyFetchAsyncImpl(apiPath, ct);
        }

        /// <summary>
        /// Gate-acquire / launch-if-cold / evaluate / lazy-reprobe-on-error flow. Made
        /// <c>protected virtual</c> so test fixtures can substitute simpler bodies (the
        /// FastIdleSigner / GatedSigner / ReprobableSigner subclasses bypass this method
        /// to avoid spinning real Chromium during unit tests).
        ///
        /// <para>
        /// B-2 path (a) — revision iteration 1 — LAZY REPROBE on EvaluateAsync error.
        /// First attempt: try captureToken on the warm page. If it throws (page navigated
        /// elsewhere, intercepted request never fired, etc.), tear down + relaunch + retry
        /// ONCE. On second throw, fall through to the existing RecordFailure path. This
        /// bounds re-entry to a single retry — no infinite recurse.
        /// </para>
        /// </summary>
        protected virtual async Task<string> ProxyFetchAsyncImpl(string apiPath, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
                }

                if (_browser == null)
                {
                    await LaunchAndProbeAsync(ct).ConfigureAwait(false);
                }

                ReArmIdleTimer();   // BEFORE Release() — Pitfall 2.

                try
                {
                    return await EvaluateProxyFetchAsync(apiPath, ct).ConfigureAwait(false);
                }
                catch (Exception evalEx) when (
                    !(evalEx is ObjectDisposedException) && !(evalEx is OperationCanceledException))
                {
                    // CR-04 mitigation (revision iteration 2): if Dispose ran concurrently
                    // (drain timeout fired and Dispose nulled fields under us → EvaluateAsync
                    // threw an NRE / closed-browser exception that landed in this catch),
                    // bail OUT of the lazy-reprobe instead of relaunching a NEW Chromium
                    // child that nothing will ever Dispose again. _disposed is set BEFORE
                    // Dispose touches the gate, so the racing-Dispose path observes
                    // _disposed = true here.
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
                    }

                    _logger.Warn(
                        "Comix signer: stale page detected (EvaluateAsync threw); relaunching and retrying once.");

                    // Invalidate the token cache — a stale page implies a stale token too.
                    _tokenCache.Clear();
                    try
                    {
                        await TeardownBrowserAsync().ConfigureAwait(false);
                        if (_disposed)
                        {
                            // CR-04 mitigation: re-check between teardown and relaunch —
                            // Dispose can have run during the await on TeardownBrowserAsync.
                            throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
                        }

                        await LaunchAndProbeAsync(ct).ConfigureAwait(false);
                        return await EvaluateProxyFetchAsync(apiPath, ct).ConfigureAwait(false);
                    }
                    catch (Exception retryEx) when (
                        !(retryEx is ObjectDisposedException) && !(retryEx is OperationCanceledException))
                    {
                        // D-10 + D-11 fall-through: second throw → RecordFailure + rethrow.
                        // (LaunchAndProbeAsync already RecordFailure'd if the relaunch's
                        // probe step failed; this is the belt-and-braces for an
                        // EvaluateAsync-only failure mode.)
                        _sourceStatusService.RecordFailure(ComixSourceKey);
                        throw;
                    }
                }
            }
            finally
            {
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Shutdown race — gate disposed by Dispose under us.
                }
            }
        }

        // ── Idle teardown timer ──────────────────────────────────────────────────────

        /// <summary>
        /// Pitfall 2: re-arm BEFORE <c>_gate.Release()</c> so the timer measures
        /// last-completed-request time, not last-started-request. The timer never fires
        /// while a request is in flight because OnIdleElapsed acquires the same gate
        /// before tearing down.
        /// </summary>
        private void ReArmIdleTimer()
        {
            if (_disposed)
            {
                return;
            }

            if (_idleTimer == null)
            {
                _idleTimer = new Timer { AutoReset = false };
                _idleTimer.Elapsed += OnIdleElapsed;
            }

            // WR-06 mitigation (revision iteration 2): bump generation BEFORE Stop +
            // Start so any already-queued Elapsed event observes a generation bump and
            // bails. The order matters — bump first, then Stop/Start — because the
            // queued Elapsed handler races with us; bumping first ensures the racing
            // handler reads a stale generation and skips its teardown.
            Interlocked.Increment(ref _idleTimerGeneration);
            _idleTimer.Stop();
            _idleTimer.Interval = IdleTimeout.TotalMilliseconds;
            _idleTimer.Start();
        }

        // W-2 (revision iteration 1): timer callback can fire after Dispose has run —
        // wrap in try/catch ObjectDisposedException to handle the fire-and-forget timer
        // + disposed gate race. async void is the canonical .NET timer-elapsed shape;
        // exceptions do NOT escape (caught + logged at Warn).
        private async void OnIdleElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // WR-06 mitigation (revision iteration 2): snapshot generation at entry.
            // If ReArmIdleTimer ran between this Elapsed event being queued to the
            // ThreadPool and us actually executing, _idleTimerGeneration has been
            // bumped — bail without tearing down (the rearm has reset the idle window).
            var generationAtEntry = Volatile.Read(ref _idleTimerGeneration);

            // WR-07 mitigation (revision iteration 2): bound the gate WaitAsync so a
            // wedged ProxyFetch (Chromium hang, network stall) cannot stall this idle
            // handler forever. Use IdleTimeout itself as the upper bound — if the gate
            // is held that long, the idle teardown is moot anyway (a request is still
            // active, so re-arm-on-completion will reset the idle window once it lands).
            bool acquired;
            try
            {
                acquired = await _gate.WaitAsync(IdleTimeout).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Timer fired after Dispose — gate already disposed; nothing to do.
                return;
            }

            if (!acquired)
            {
                _logger.Debug("Comix signer: idle teardown skipped — gate busy beyond IdleTimeout.");
                return;
            }

            try
            {
                // WR-06 mitigation: re-check generation under the gate. Even if we
                // observed an unbumped generation at entry, ReArmIdleTimer can have run
                // between then and now (we yielded on the WaitAsync). Re-checking under
                // the gate ensures we never tear down a browser whose idle window was
                // reset.
                if (Volatile.Read(ref _idleTimerGeneration) != generationAtEntry)
                {
                    return;
                }

                if (_disposed)
                {
                    return;
                }

                if (_browser != null)
                {
                    _logger.Info("Comix signer: idle {0} min, browser torn down.", IdleTimeout.TotalMinutes);
                    await TeardownBrowserAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Comix signer: idle teardown raised; ignoring (browser will cold-spawn on next request).");
            }
            finally
            {
                try
                {
                    _gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Race-safe: Dispose can run concurrently with this handler.
                }
            }
        }

        /// <summary>
        /// Pitfall 4: null fields BEFORE awaiting CloseAsync (race-safety). Same shape used
        /// by the lazy-reprobe path in Task 1b (B-2 path (a)).
        /// </summary>
        private async Task TeardownBrowserAsync()
        {
            var browser = _browser;
            _browser = null;
            _page = null;
            _cachedUserAgent = null;
            _tokenCache.Clear();

            if (browser != null)
            {
                try
                {
                    await browser.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Comix signer: browser.CloseAsync raised during teardown; ignoring.");
                }
            }
        }

        // ── Shutdown / disposal ──────────────────────────────────────────────────────

        public void Handle(ApplicationShutdownRequested message)
        {
            _logger.Info("Comix signer: ApplicationShutdownRequested received; disposing browser.");
            Dispose();
        }

        /// <summary>
        /// W-2 (revision iteration 1): explicit drain-then-dispose semantics. Synchronous
        /// wait inside Dispose is acceptable here — shutdown path; the alternative is
        /// async-Dispose plumbing across the IHandle&lt;&gt; bus surface that Mangarr's
        /// existing dispose-on-shutdown sites (e.g. <c>DatabaseTarget.UnRegister</c>) do
        /// not adopt either.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Stop + dispose the idle timer first so it cannot fire mid-dispose.
            if (_idleTimer != null)
            {
                _idleTimer.Stop();
                _idleTimer.Elapsed -= OnIdleElapsed;
                try
                {
                    _idleTimer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Comix signer: idle timer Dispose raised; ignoring.");
                }

                _idleTimer = null;
            }

            // Wait up to 5s for any in-flight ProxyFetchAsync to release the gate.
            bool acquired;
            try
            {
                acquired = _gate.Wait(DisposeDrainTimeout);
            }
            catch (ObjectDisposedException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                _logger.Warn("Comix signer: shutdown timed out waiting for in-flight request — forcing teardown");
            }

            // Pitfall 4: null fields BEFORE the (sync) browser-close.
            var browser = _browser;
            _browser = null;
            _page = null;
            _cachedUserAgent = null;
            _tokenCache.Clear();

            if (browser != null)
            {
                try
                {
                    browser.CloseAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    // Swallow at shutdown — the process is going down.
                }
            }

            if (acquired)
            {
                try
                {
                    _gate.Release();
                }
                catch (SemaphoreFullException)
                {
                    // No-op: race with the (gone) in-flight call.
                }
                catch (ObjectDisposedException)
                {
                    // No-op: Dispose-after-Dispose race.
                }
            }

            try
            {
                _gate.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Comix signer: gate Dispose raised; ignoring.");
            }
        }
    }
}
