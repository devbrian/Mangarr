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
    // signer for comix.to (replaces the static ComixHash port broken 2026-05-10 by upstream
    // key rotation + response-body encryption — see .planning/debug/comix-invalid-token-403.md).
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

        // ── Test-overridable seams (D-12 / fixture overrides) ────────────────────────

        /// <summary>
        /// D-12: hardcoded const for v1.0; <c>protected virtual</c> so test fixtures can
        /// override (see <c>ComixSignerIdleTeardownFixture.FastIdleSigner</c> per the
        /// fixture-Wave-1 elaboration shape).
        /// </summary>
        protected virtual TimeSpan IdleTimeout => TimeSpan.FromMinutes(10);

        /// <summary>
        /// Page-context impl seams. Stubbed at Task 1a (lifecycle skeleton); filled at
        /// Task 1b. Test fixtures override via subclass for FailSoft / IdleTeardown /
        /// LazyReprobe scenarios — overrides do NOT need a real Chromium child.
        /// </summary>
        protected virtual Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
            => throw new NotImplementedException("Plan 17-02 Task 1b owns this method body.");

        protected virtual Task LaunchAndProbeAsync(CancellationToken ct)
            => throw new NotImplementedException("Plan 17-02 Task 1b owns this method body.");

        protected virtual Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
            => throw new NotImplementedException("Plan 17-02 Task 1b owns this method body.");

        // ── Fields ────────────────────────────────────────────────────────────────────
        private readonly IIndexerSourceStatusService _sourceStatusService;
        private readonly Logger _logger;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _disposed;

        // _probeFailureCount is read inside Task 1b's LaunchAndProbeAsync — it is incremented
        // on each probe failure and surfaces in the D-15 Warn line. Task 1a leaves this field
        // declared (so the dispose/teardown path needs no field-shape change at Task 1b time)
        // and the analyzer warning is suppressed via #pragma until Task 1b reads it.
#pragma warning disable CS0169 // The field is never used (resolved at Task 1b)
        private int _probeFailureCount;
#pragma warning restore CS0169

        // Page-context state — mutated only under _gate.
        // Pitfall 4: nulled BEFORE awaiting browser.CloseAsync inside TeardownBrowserAsync.
        // _page / _signerExpr / _installerExpr are written in Task 1a (TeardownBrowserAsync
        // + Dispose null them out); Task 1b adds the read-side (LaunchAndProbeAsync sets +
        // EvaluateProxyFetchAsync reads). CS0414 (assigned-but-never-read) is suppressed
        // until Task 1b lands the read side.
        private IBrowser _browser;
#pragma warning disable CS0414 // The field is assigned but its value is never used (resolved at Task 1b)
        private IPage _page;
        private string _signerExpr;
        private string _installerExpr;
#pragma warning restore CS0414

        private Timer _idleTimer;

        public ComixPuppeteerSigner(IIndexerSourceStatusService sourceStatusService, Logger logger)
        {
            _sourceStatusService = sourceStatusService;
            _logger = logger;
        }

        // ── Public surface ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sign-and-fetch the given comix.to API path through the warm Chromium page; returns
        /// the decoded JSON body. Task 1b fills in the gate-acquire / launch / evaluate /
        /// reprobe flow.
        /// </summary>
        public Task<string> ProxyFetchAsync(string apiPath, CancellationToken ct = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ComixPuppeteerSigner));
            }

            return ProxyFetchAsyncImpl(apiPath, ct);
        }

        // Task 1b owns the body. Made virtual so subclasses (test fixtures) can drive the
        // lifecycle without needing the real page-context impl.
        protected virtual Task<string> ProxyFetchAsyncImpl(string apiPath, CancellationToken ct)
            => throw new NotImplementedException("Plan 17-02 Task 1b owns this method body.");

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
