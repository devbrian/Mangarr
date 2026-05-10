using System;
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
    /// Process-singleton runtime signer for comix.to. Mirrors keiyoushi <c>Signer.kt</c>
    /// (Apache-2.0). Owns an embedded headless Chromium child process (PuppeteerSharp
    /// 24.42.0): lazy-spawn warm page, behaviour-based namespace probe (D-14), idle-teardown
    /// after 10 min (D-12), clean shutdown via
    /// <see cref="ApplicationShutdownRequested"/>. Auto-registered <see cref="DryIoc.Reuse"/>.
    /// <see cref="DryIoc.Reuse.Singleton"/> via the existing
    /// <c>NzbDrone.Common/Composition/Extensions.cs:25-35</c> RegisterMany convention.
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
        private const string ComixHomepageUrl = "https://comix.to/";

        // PROBE_JS — ports keiyoushi Signer.kt verbatim (D-14). Behaviour-based: detects
        // signer (function returning ≥40-char base64url) + installer (function registering a
        // response interceptor on a fake axios) inside any window.vmf_* namespace. Names
        // rotate per comix.to deploy; behaviour does not.
        private const string PROBE_JS = @"
          (() => {
            const probe = (probePath) => {
              let signerExpr = null, installerExpr = null;
              for (const ns of Object.keys(window).filter(k => k.startsWith('vmf_'))) {
                const obj = window[ns];
                for (const fn of Object.keys(obj || {})) {
                  try {
                    const out = obj[fn](probePath);
                    if (typeof out === 'string' && out.length >= 40 && /^[A-Za-z0-9_-]+$/.test(out)) {
                      signerExpr = ns + '.' + fn;
                      continue;
                    }
                  } catch (_e) {}
                  try {
                    let got = false;
                    const fakeAxios = {
                      interceptors: {
                        response: { use: () => { got = true; } },
                        request:  { use: () => {} },
                      },
                    };
                    obj[fn](fakeAxios);
                    if (got) installerExpr = ns + '.' + fn;
                  } catch (_e) {}
                }
              }
              return { signerExpr: signerExpr, installerExpr: installerExpr };
            };
            return probe('/manga/__probe__/chapters');
          })();
        ";

        // ── Static readonly fields ───────────────────────────────────────────────────
        // W-2 (revision iteration 1): drain timeout for in-flight requests on Dispose.
        private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

        // V5 input validation per RESEARCH §"Security Domain" / T-17-02-01.
        // Pre-compiled for hot-path performance; constraint: path-only, no scheme/host/..
        private static readonly System.Text.RegularExpressions.Regex ApiPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/[A-Za-z0-9_\\-/]+(\\?[A-Za-z0-9_\\-/=&%.]*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // WR-02 mitigation (revision iteration 2): defense-in-depth allowlist on the
        // probe-captured signerExpr/installerExpr strings BEFORE they are interpolated
        // into the in-page JS template. JS object property names CAN technically be
        // arbitrary strings; bundlers in practice emit alphanumeric keys (`vmf_<hex>`
        // namespace + identifier-shaped function name). Anything outside that shape is
        // either an upstream rotation we don't recognize OR an MITM rewrite — reject
        // either way and surface as a probe failure (D-15 RecordFailure path).
        private static readonly System.Text.RegularExpressions.Regex SignerExprAllowlistRegex =
            new System.Text.RegularExpressions.Regex(
                @"^vmf_[A-Za-z0-9_$]{1,128}\.[A-Za-z0-9_$]{1,128}$",
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
            // Pitfall 7 (Chromium-as-root in container): launch args required for headless
            // Chrome inside Docker — --no-sandbox + setuid disable + dev/shm fallback +
            // GPU disable. ExecutablePath comes from PUPPETEER_EXECUTABLE_PATH (D-03 escape
            // hatch) or the baked image-layer path under /opt/mangarr-chromium (D-03 default).
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

        protected virtual async Task LaunchAndProbeAsync(CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _browser = await LaunchBrowserAsync(ct).ConfigureAwait(false);
                _page = await _browser.NewPageAsync().ConfigureAwait(false);
                await _page.GoToAsync(ComixHomepageUrl).ConfigureAwait(false);

                var probe = await _page.EvaluateExpressionAsync<ProbeResult>(PROBE_JS).ConfigureAwait(false);
                if (probe == null
                    || string.IsNullOrEmpty(probe.SignerExpr)
                    || string.IsNullOrEmpty(probe.InstallerExpr))
                {
                    throw new InvalidOperationException(
                        "Comix signer: probe failed; window.vmf_* signer/installer fns not found.");
                }

                // WR-02 mitigation: defense-in-depth allowlist on the probe-captured
                // expressions. The strings are interpolated unescaped into
                // EvaluateProxyFetchAsync's JS template; an MITM-rewritten or
                // upstream-rotated key shape outside our identifier whitelist must NOT
                // reach that interpolation. Reject as a probe failure (D-15 path).
                if (!SignerExprAllowlistRegex.IsMatch(probe.SignerExpr)
                    || !SignerExprAllowlistRegex.IsMatch(probe.InstallerExpr))
                {
                    throw new InvalidOperationException(
                        "Comix signer: probe captured signer/installer expression(s) outside the identifier allowlist; rejecting as drift or MITM.");
                }

                _signerExpr = probe.SignerExpr;
                _installerExpr = probe.InstallerExpr;
                _probeFailureCount = 0;
                sw.Stop();

                _logger.Info(
                    "Comix signer: Chromium launched, page loaded, probe captured signer/installer fns (probe latency: {0}ms).",
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

        protected virtual async Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
        {
            // CR-05 mitigation (revision iteration 2): snapshot fields under the gate before
            // any await against the page so a concurrent Dispose-after-drain-timeout that
            // nulls _page / _signerExpr / _installerExpr cannot NRE us mid-evaluate. The
            // gate's own contract enforces single-writer; the snapshot guards the read-
            // before-await window. We additionally re-check _disposed (set by Dispose
            // BEFORE it touches the gate) so a racing dispose bails cleanly with
            // ObjectDisposedException instead of an NRE bubbling up the lazy-reprobe catch.
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            var page = _page;
            var signerExpr = _signerExpr;
            var installerExpr = _installerExpr;
            if (page == null || string.IsNullOrEmpty(signerExpr) || string.IsNullOrEmpty(installerExpr))
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            // CR-01 + CR-02 mitigation (revision iteration 2): mirror upstream Signer.kt
            // proxyFetch shape verbatim (Resources/upstream-signer.txt:76-117).
            //  - Capture BOTH request + response interceptors registered by installer().
            //  - Sign extractSignablePath(apiPath) — apiPath.split('?')[0] (CR-02 fix:
            //    upstream signs the path WITHOUT the query string per
            //    Resources/upstream-signer.txt:124-125).
            //  - Append the token to the full apiPath (with query string preserved).
            //  - On encrypted-body shape (`'e' in raw && captured.res`), build a fakeResp
            //    and await captured.res(fakeResp) to obtain decoded.data; wrap as
            //    `{result: <decoded>}` matching upstream bodyOut shape (CR-01 fix).
            //
            // Cached signerExpr / installerExpr are validated by an alphanumeric-only
            // allowlist regex at probe time (WR-02 mitigation in LaunchAndProbeAsync) so
            // the substitution below is JS-injection-safe even if a future comix.to
            // deploy emits a hostile object key. JsString escapes the apiPath value
            // (T-17-02-02 mitigation: JS-injection-safe single-quote string substitution).
            var jsTemplate = $@"
              (async () => {{
                const captured = {{ req: null, res: null }};
                const fakeAxios = {{
                  interceptors: {{
                    request:  {{ use: function(fn) {{ captured.req = fn; }} }},
                    response: {{ use: function(fn) {{ captured.res = fn; }} }}
                  }},
                  defaults: {{ headers: {{ common: {{}} }}, transformRequest: [], transformResponse: [] }}
                }};
                const signer = {signerExpr};
                const installer = {installerExpr};
                installer(fakeAxios);

                const apiPath = {JsString(apiPath)};
                const signablePath = apiPath.split('?')[0];
                const token = signer(signablePath);
                const sep = apiPath.indexOf('?') === -1 ? '?' : '&';
                const url = '/api/v1' + apiPath + sep + '_=' + encodeURIComponent(token);
                const resp = await fetch(url, {{
                  credentials: 'include',
                  headers: {{ 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }}
                }});
                const text = await resp.text();
                let raw;
                try {{ raw = JSON.parse(text); }} catch (_e) {{ raw = null; }}
                if (raw && typeof raw === 'object' && 'e' in raw && captured.res) {{
                  const fakeResp = {{
                    data: raw,
                    status: resp.status,
                    statusText: resp.statusText,
                    headers: Object.fromEntries([...resp.headers.entries()]),
                    config: {{ url: url, method: 'get', baseURL: '/api/v1' }},
                    request: {{}}
                  }};
                  const decoded = await captured.res(fakeResp);
                  return JSON.stringify({{ result: decoded && decoded.data }});
                }}
                return text;
              }})();";

            return await page.EvaluateExpressionAsync<string>(jsTemplate).ConfigureAwait(false);
        }

        // T-17-02-02 mitigation: single-quote string substitution helper. Escapes \ and '
        // before string-format-style insertion into the page-context JS template.
        private static string JsString(string s) =>
            "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        // BrowserFetcher discovery for D-03 baked Chromium under /opt/mangarr-chromium
        // (or PUPPETEER_CACHE_DIR if the operator overrode the cache dir). Returns null
        // when no installed browser found — Puppeteer.LaunchAsync will then surface its
        // own error (caught + RecordFailure'd by LaunchAndProbeAsync).
        private static string GetBakedChromiumPath()
        {
            var cacheDir = Environment.GetEnvironmentVariable("PUPPETEER_CACHE_DIR")
                           ?? "/opt/mangarr-chromium";
            try
            {
                var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = cacheDir });
                var installed = System.Linq.Enumerable.FirstOrDefault(fetcher.GetInstalledBrowsers());
                return installed?.GetExecutablePath();
            }
            catch
            {
                return null;
            }
        }

        // ProbeResult — JSON-serialisation target for the PROBE_JS return value.
        private sealed class ProbeResult
        {
            [System.Text.Json.Serialization.JsonPropertyName("signerExpr")]
            public string SignerExpr { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("installerExpr")]
            public string InstallerExpr { get; set; }
        }

        // ── Fields ────────────────────────────────────────────────────────────────────
        private readonly IIndexerSourceStatusService _sourceStatusService;
        private readonly Logger _logger;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // _probeFailureCount tracks the n-count surfaced in the D-15 probe-failure Warn
        // line. Reset to 0 on a successful probe; incremented on each probe throw.
        private int _probeFailureCount;

        // Page-context state — mutated only under _gate.
        // Pitfall 4: nulled BEFORE awaiting browser.CloseAsync inside TeardownBrowserAsync.
        private IBrowser _browser;
        private IPage _page;
        private string _signerExpr;
        private string _installerExpr;

        private Timer _idleTimer;

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
        /// First attempt: try the cached (signerExpr, installerExpr) on the warm page.
        /// If it throws (stale cache because comix.to re-deployed mid-session), tear down
        /// + relaunch + retry ONCE. On second throw, fall through to the existing
        /// RecordFailure path. This bounds re-entry to a single retry — no infinite recurse.
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
                    _logger.Warn(
                        "Comix signer: stale page detected (EvaluateAsync threw); relaunching and retrying once.");
                    try
                    {
                        await TeardownBrowserAsync().ConfigureAwait(false);
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
            try
            {
                await _gate.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Timer fired after Dispose — gate already disposed; nothing to do.
                return;
            }

            try
            {
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
            _signerExpr = null;
            _installerExpr = null;

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
            _signerExpr = null;
            _installerExpr = null;

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
