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

        // PROBE_JS — behaviour-based detection of comix.to's anti-bot signer and axios
        // installer (D-14). Mirrors keiyoushi Signer.kt's intent (Apache-2.0). Detects:
        //   - signer:    fn(path) -> ≥40-char base64url string (different from input)
        //   - installer: fn(axios) registers a response interceptor on a fake axios
        // and the upstream Signer.kt comment "Names rotate per deploy; behaviour does not"
        // means BOTH the namespace prefix AND function names rotate. The earlier port
        // hardcoded `window.<vmf_*>.*`, but comix.to has since rotated to `vmX_<hex>`
        // (observed live 2026-05-10) — drop the prefix filter and walk all window
        // namespaces. The combined signer + installer behaviour is a strong enough
        // filter on its own; a typical page exposes ~270 window keys but only one
        // pair will have both behaviours.
        //
        // SignerExprAllowlistRegex (defense in depth, WR-02) constrains the captured
        // names to identifier shape before they're interpolated into the in-page JS
        // template, so any non-bundle namespace that accidentally passes the
        // behaviour test is still rejected at the validation gate.
        //
        // GAP-17-C fix (Plan 17-07 Task 1): same-namespace gating — signer + installer
        // MUST be discovered in the same `Object.keys(window)` outer-loop iteration. The
        // earlier shape allowed cross-namespace pairs (signer from `ns_A`, installer from
        // `ns_B`) which could produce mismatched function pairs from different bundles.
        // The fix moves the `signerExpr`/`installerExpr` capture into per-namespace
        // locals (`nsSigner` / `nsInstaller`); only when BOTH fire in the same iteration
        // do they get committed to the outer `outerSignerExpr` / `outerInstallerExpr`
        // and the walk terminates. Partial captures from non-pairing namespaces are
        // dropped before moving on. See ComixSignerProbeSameNamespaceFixture for the
        // Chromium-free regression guard locking this contract.
        private const string PROBE_JS = @"
          (() => {
            const probe = (probePath) => {
              let outerSignerExpr = null, outerInstallerExpr = null;
              for (const ns of Object.keys(window)) {
                const obj = window[ns];
                if (!obj || typeof obj !== 'object') continue;
                let fns;
                try { fns = Object.keys(obj); } catch (_e) { continue; }
                if (fns.length === 0 || fns.length > 200) continue;

                let nsSigner = null, nsInstaller = null;
                for (const fn of fns) {
                  if (nsSigner === null) {
                    try {
                      const out = obj[fn](probePath);
                      if (typeof out === 'string'
                          && out !== probePath
                          && out.length >= 40
                          && /^[A-Za-z0-9_-]+$/.test(out)) {
                        nsSigner = ns + '.' + fn;
                        continue;
                      }
                    } catch (_e) {}
                  }
                  if (nsInstaller === null) {
                    try {
                      let got = false;
                      const fakeAxios = {
                        interceptors: {
                          response: { use: () => { got = true; } },
                          request:  { use: () => {} },
                        },
                        defaults: { headers: { common: {} }, transformRequest: [], transformResponse: [] },
                      };
                      obj[fn](fakeAxios);
                      if (got) nsInstaller = ns + '.' + fn;
                    } catch (_e) {}
                  }
                  if (nsSigner !== null && nsInstaller !== null) break;
                }
                // Same-namespace gate (GAP-17-C): ONLY commit the pair when BOTH locals
                // fire in this `ns` iteration. Otherwise drop nsSigner / nsInstaller and
                // continue to the next namespace — partial captures must NOT cross the
                // outer-loop boundary.
                if (nsSigner !== null && nsInstaller !== null) {
                  outerSignerExpr = nsSigner;
                  outerInstallerExpr = nsInstaller;
                  break;
                }
              }
              return { signerExpr: outerSignerExpr, installerExpr: outerInstallerExpr };
            };
            return probe('/manga/__probe__/chapters');
          })();
        ";

        /// <summary>
        /// Plan 17-07 Task 1 test seam: exposes the PROBE_JS const to the
        /// <see cref="T:NzbDrone.Core.Test.Indexers.Comix.ComixSignerProbeSameNamespaceFixture"/>
        /// Chromium-free fixture so the same-namespace contract can be verified by
        /// file-text grep without a real browser. NOT called from production code.
        /// </summary>
        // Sonarr divergence: test-only accessor; Mangarr-only signer seam (Pattern ι allowlist).
        internal static string GetProbeJsForTest() => PROBE_JS;

        // ── Static readonly fields ───────────────────────────────────────────────────
        // W-2 (revision iteration 1): drain timeout for in-flight requests on Dispose.
        private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

        // V5 input validation per RESEARCH §"Security Domain" / T-17-02-01.
        // Pre-compiled for hot-path performance; constraint: path-only, no scheme/host/..
        private static readonly System.Text.RegularExpressions.Regex ApiPathRegex =
            new System.Text.RegularExpressions.Regex(
                "^/[A-Za-z0-9_\\-/]+(\\?[A-Za-z0-9_\\-/=&%.]*)?$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // WR-02 mitigation: defense-in-depth allowlist on the probe-captured
        // signerExpr/installerExpr strings BEFORE they are interpolated into the
        // in-page JS template. JS object property names CAN technically be arbitrary
        // strings; bundlers in practice emit identifier-shaped keys. Anything
        // outside that shape is either an upstream rotation we don't recognize OR
        // an MITM rewrite — reject either way and surface as a probe failure
        // (D-15 RecordFailure path).
        //
        // Earlier revision required a `vmf_` namespace prefix; comix.to rotated
        // to `vmX_<hex>` (observed live 2026-05-10), so the allowlist no longer
        // pins the prefix. PROBE_JS's behavioural detection (signer + installer
        // both required) plus this identifier-shape gate together provide the
        // safety envelope.
        private static readonly System.Text.RegularExpressions.Regex SignerExprAllowlistRegex =
            new System.Text.RegularExpressions.Regex(
                @"^[A-Za-z_$][A-Za-z0-9_$]{0,127}\.[A-Za-z_$0-9][A-Za-z0-9_$]{0,127}$",
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
                // WR-03 mitigation: observe ct between each await. PuppeteerSharp's
                // NewPageAsync / GoToAsync / EvaluateExpressionAsync don't accept ct
                // directly; throw-on-request between calls is the best we get.
                _browser = await LaunchBrowserAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                _page = await _browser.NewPageAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                // WaitUntilNavigation.Networkidle0 — comix.to fronts a Cloudflare
                // interstitial that responds 502 on initial load before the bundle
                // finishes; the default Load event fires on the partial page and
                // window.<bundle-namespace> isn't populated yet. Networkidle0 waits
                // for ≥500ms of zero in-flight requests, which lets the bundle
                // finish parsing and registering its `vmX_<hex>` namespace before
                // PROBE_JS runs.
                await _page.GoToAsync(
                    ComixHomepageUrl,
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle0 } })
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var probe = await _page.EvaluateExpressionAsync<ProbeResult>(PROBE_JS).ConfigureAwait(false);

                // Phase 17.2 D-1 (Mitigation A): settle the page execution context after PROBE_JS
                // so subsequent EvaluateExpressionAsync calls in EvaluateProxyFetchAsync hit a
                // stable context, not one mid-Runtime.executionContextDestroyed from PROBE_JS's
                // pushState side-effect. Per Phase 17 17-08-LIVE-VERIFICATION-EVIDENCE.md
                // Finding 3 (orchestrator-driven Playwright re-verification 2026-05-10), running
                // PROBE_JS against the live page changes the URL from `https://comix.to/` to
                // `https://comix.to/[object%20Object]` — PROBE_JS indiscriminately calls every
                // property of every window object with the probe path string, and at least one
                // of those calls is a router function whose stringified-Object argument triggers
                // a soft pushState. PuppeteerSharp 24.42.0 interprets the resulting
                // `Page.frameNavigated` / `Runtime.executionContextDestroyed` event as a hard
                // "context destroyed" before the next EvaluateExpressionAsync can run.
                //
                // Settle flavor chosen: WaitForNetworkIdleAsync(IdleTime=500ms, Timeout=5000ms).
                // Rationale: a soft pushState does NOT necessarily generate network requests, so
                // WaitForNavigationAsync's Networkidle0/Load wait conditions may never trigger
                // and we'd time out spuriously even when no settle is actually needed. By
                // contrast, WaitForNetworkIdleAsync is the safest superset — it converges to a
                // stable signal regardless of whether pushState fired (when no nav occurred,
                // network is already idle and the call returns near-instantly; when pushState
                // fired, we wait the actual settle window). Polling EvaluateExpressionAsync<bool>
                // (Option c per the plan) was rejected because it invokes the very
                // EvaluateExpressionAsync call Mitigation A is trying to make safe, defeating
                // the purpose. T-17.2-01 mitigation: bounded by the 5000ms Timeout (no infinite
                // wait, no DoS surface).
                //
                // On any thrown exception (TimeoutException, ObjectDisposedException, etc.) the
                // existing catch (lines 268-275) handles it: _probeFailureCount increments,
                // RecordFailure(ComixSourceKey) fires, TeardownBrowserAsync runs, throw rethrows.
                // No new catch added; no swallowing.
                //
                // PROBE_JS string literal byte-for-byte unchanged (D-14 inherited / SC#5).
                // Sonarr divergence: Mangarr-only seam (Pattern S2 / sonarr-consistency-audit
                // Pattern ι allowlist coverage). See:
                //   .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-08-LIVE-VERIFICATION-EVIDENCE.md (Finding 3)
                //   .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-LEARNINGS.md (L-1, S-2)
                //   .planning/phases/17.2-comix-signer-driver-layer-fix/17.2-CONTEXT.md (D-1)
                await _page.WaitForNetworkIdleAsync(
                        new WaitForNetworkIdleOptions { IdleTime = 500, Timeout = 5000 })
                    .ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

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

        // GAP-17-B Branch C (Plan 17-06): the production IIFE's `await captured.res(fakeResp)`
        // decrypt invocation reliably destroys the warm Chromium page execution context on
        // comix.to (diagnosed via Plan 17-05's Probe D — fetch-no-decrypt — returning 200 OK
        // + `{"e":"..."}` envelope cleanly while the production path with the decrypt step
        // fires the lazy-reprobe Warn on every WarmAsync). The decrypt step is wrapped in an
        // in-page `try/catch`: on throw, the IIFE returns a JSON envelope marking
        // `decryptError` instead of letting the rejection tear the page down. The .NET caller
        // (ComixIndexer.Fetch) sees the same string return shape — its existing JSON parser
        // surfaces a parse error to IIndexerSourceStatusService.RecordFailure escalation if
        // the decrypt always fails. Followup issue tracked against Phase 17.2 milestone.
        // Annotation block remains so future maintainers don't "simplify" the catch back to a
        // bare await — see UpstreamSignerDriftFixture.Port_must_not_regress_GAP_17_B_decrypt_guard.
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
                  // GAP-17-B Branch C fix (Plan 17-06): wrap the captured response interceptor
                  // invocation in try/catch. Plan 17-05's Probe D diagnosis showed that
                  // `await captured.res(fakeResp)` mutates window state (likely
                  // window.location reload on stale-token defence OR a CSP-violating
                  // side-effect) that destroys the page execution context. The catch
                  // surfaces the encrypted shape + a `decryptError` string to the .NET
                  // caller so ComixIndexer.Fetch's existing JSON-parse path can route to
                  // IIndexerSourceStatusService.RecordFailure escalation, and — critically —
                  // the warm page stays alive for the next request.
                  try {{
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
                  }} catch (decryptErr) {{
                    return JSON.stringify({{ result: null, e: raw.e, decryptError: String(decryptErr) }});
                  }}
                }}
                return text;
              }})();";

            // WR-03 mitigation: cancellation observation BEFORE the page evaluation —
            // PuppeteerSharp.EvaluateExpressionAsync does not accept ct in 24.42.0,
            // but a fast-fail on cancellation here saves the ~25s in-page fetch timeout.
            ct.ThrowIfCancellationRequested();
            return await page.EvaluateExpressionAsync<string>(jsTemplate).ConfigureAwait(false);
        }

        /// <summary>
        /// Phase 17 GAP-17-B diagnostic seam (Plan 17-05 Task 1). Wraps the warm page's
        /// <see cref="IPage.EvaluateExpressionAsync{T}(string)"/> for the diagnostic fixture
        /// in <c>Mangarr.Comix.Live.Test</c> to drive raw probes against the SAME warm
        /// page used by the production code path. NOT called from production code.
        /// Fails fast with <see cref="ObjectDisposedException"/> if the page is gone.
        /// </summary>
        /// <remarks>
        /// `protected internal` so the diagnostic fixture (which lives in a sibling test
        /// project, NOT in NzbDrone.Core.Test) can subclass and access it. The seam is
        /// VIRTUAL only to satisfy fixture-override patterns elsewhere in this class — no
        /// production override is intended.
        /// </remarks>
        protected internal virtual async Task<string> EvaluateRawAsync(string js, CancellationToken ct)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            var page = _page;
            if (page == null)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            ct.ThrowIfCancellationRequested();
            return await page.EvaluateExpressionAsync<string>(js).ConfigureAwait(false);
        }

        /// <summary>
        /// Phase 17 GAP-17-B diagnostic accessor (Plan 17-05 Task 1). Exposes the
        /// captured signer namespace expression so the diagnostic fixture can construct
        /// stepwise probes WITHOUT reflection (a future rename of `_signerExpr` will
        /// surface as a compile error rather than silently breaking the diagnostic).
        /// `protected internal` so the diagnostic fixture in the sibling test project
        /// can subclass and read. NOT called from production code.
        /// </summary>
        protected internal string SignerExprForTest => _signerExpr;

        /// <summary>
        /// Phase 17 GAP-17-B diagnostic accessor (Plan 17-05 Task 1). Exposes the
        /// captured installer namespace expression so the diagnostic fixture can
        /// construct stepwise probes WITHOUT reflection. NOT called from production code.
        /// </summary>
        protected internal string InstallerExprForTest => _installerExpr;

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
        // line. Reset to 0 on a successful probe; incremented on each probe throw.
        private int _probeFailureCount;

        // Page-context state — mutated only under _gate.
        // Pitfall 4: nulled BEFORE awaiting browser.CloseAsync inside TeardownBrowserAsync.
        private IBrowser _browser;
        private IPage _page;
        private string _signerExpr;
        private string _installerExpr;

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
