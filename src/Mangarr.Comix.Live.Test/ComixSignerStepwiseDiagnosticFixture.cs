using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// Phase 17 GAP-17-B diagnostic fixture (Plan 17-05 Task 2). Runs FOUR stepwise
    /// probes against the same warm comix.to page that the production fixture uses,
    /// isolating which sub-step of <see cref="ComixPuppeteerSigner.EvaluateProxyFetchAsync"/>
    /// destroys the page context.
    ///
    /// <para>
    /// The four probes (each in its own [Test]) are layered:
    ///   - Probe A: `(() => 'a-ok')()` — raw page liveness sanity check.
    ///   - Probe B: install-only — register fake-axios, capture interceptors, return
    ///     boolean indicating capture; NO fetch, NO sign.
    ///   - Probe C: sign-no-fetch — invoke signer(signablePath), return token shape;
    ///     NO fetch.
    ///   - Probe D: fetch-no-decrypt — full sign + fetch, return raw text body; NO
    ///     decryption call. (Matches the FIRST half of EvaluateProxyFetchAsync.)
    /// </para>
    ///
    /// <para>
    /// If Probe D throws `Execution context was destroyed`, the fetch itself is the
    /// culprit (Cloudflare bot-detect or URL-prefix mismatch). If only the production
    /// `EvaluateProxyFetchAsync` (with the captured-res invocation) throws, the
    /// `await captured.res(fakeResp)` is the culprit.
    /// </para>
    ///
    /// <para>
    /// Excluded from `bash scripts/test.sh` runs by [LiveComix]; runs locally OR in
    /// the daily-soak workflow. CI never runs it (D-17).
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("Plan 17-05 diagnostic harness — runs only when explicitly invoked or via daily-soak.")]
    public class ComixSignerStepwiseDiagnosticFixture : TestBase<DiagnosticHarnessSigner>
    {
        [Test]
        public async Task Probe_A_raw_page_eval_returns_string_after_warm_spawn()
        {
            // Force the production warm-page spawn + probe path so the fixture exercises
            // the same Chromium child + Networkidle0 wait the production fixture uses.
            await Subject.WarmAsync(CancellationToken.None);

            var result = await Subject.EvaluateRawDiagnostic("(() => 'a-ok')()", CancellationToken.None);

            result.Should().Be("a-ok",
                "Probe A: warm page must accept raw EvaluateExpressionAsync calls; " +
                "if this fails, the failure is upstream of the IIFE entirely.");
        }

        [Test]
        public async Task Probe_B_install_only_captures_interceptors_without_fetch()
        {
            await Subject.WarmAsync(CancellationToken.None);

            // Probe B: register fake-axios via the captured installer; assert capture.res is set.
            // Mirrors EvaluateProxyFetchAsync's first 9 lines but stops BEFORE the fetch.
            var probe = $@"
              (() => {{
                const captured = {{ req: null, res: null }};
                const fakeAxios = {{
                  interceptors: {{
                    request:  {{ use: function(fn) {{ captured.req = fn; }} }},
                    response: {{ use: function(fn) {{ captured.res = fn; }} }}
                  }},
                  defaults: {{ headers: {{ common: {{}} }}, transformRequest: [], transformResponse: [] }}
                }};
                const installer = {Subject.InstallerExprForDiagnostic};
                installer(fakeAxios);
                return JSON.stringify({{
                  hasReq: typeof captured.req === 'function',
                  hasRes: typeof captured.res === 'function'
                }});
              }})();";

            var result = await Subject.EvaluateRawDiagnostic(probe, CancellationToken.None);

            result.Should().Contain("\"hasRes\":true",
                "Probe B: installer must register a response interceptor for decryption to be possible. " +
                "If hasRes is false, GAP-17-B is upstream — the installer itself isn't capturing.");
        }

        [Test]
        public async Task Probe_C_sign_only_returns_token_without_fetch()
        {
            await Subject.WarmAsync(CancellationToken.None);

            // Probe C: invoke signer on a known good path; assert token shape.
            var probe = $@"
              (() => {{
                const signer = {Subject.SignerExprForDiagnostic};
                const out = signer('/manga/mr3m0/chapters');
                return JSON.stringify({{ ok: typeof out === 'string', length: out.length }});
              }})();";

            var result = await Subject.EvaluateRawDiagnostic(probe, CancellationToken.None);

            result.Should().Contain("\"ok\":true",
                "Probe C: signer(signablePath) must return a string; " +
                "if not, namespace probe captured the wrong fn.");
        }

        [Test]
        public async Task Probe_D_fetch_no_decrypt_returns_body_or_pinpoints_fetch_destruction()
        {
            await Subject.WarmAsync(CancellationToken.None);

            // Probe D: replicate EvaluateProxyFetchAsync up to and INCLUDING the fetch,
            // but STOP before the decrypt invocation. If this throws `Execution context
            // was destroyed`, the fetch itself is the culprit (Cloudflare or URL prefix).
            // If it returns a body, then the failure is in the decrypt step.
            const string ApiPath = "/manga/mr3m0/chapters";
            var probe = $@"
              (async () => {{
                const captured = {{ req: null, res: null }};
                const fakeAxios = {{
                  interceptors: {{
                    request:  {{ use: function(fn) {{ captured.req = fn; }} }},
                    response: {{ use: function(fn) {{ captured.res = fn; }} }}
                  }},
                  defaults: {{ headers: {{ common: {{}} }}, transformRequest: [], transformResponse: [] }}
                }};
                const signer = {Subject.SignerExprForDiagnostic};
                const installer = {Subject.InstallerExprForDiagnostic};
                installer(fakeAxios);
                const apiPath = '{ApiPath}';
                const signablePath = apiPath.split('?')[0];
                const token = signer(signablePath);
                const sep = apiPath.indexOf('?') === -1 ? '?' : '&';
                const url = '/api/v1' + apiPath + sep + '_=' + encodeURIComponent(token);
                let stage = 'pre-fetch';
                try {{
                  stage = 'fetching';
                  const resp = await fetch(url, {{
                    credentials: 'include',
                    headers: {{ 'Accept': 'application/json', 'X-Requested-With': 'XMLHttpRequest' }}
                  }});
                  stage = 'reading-text';
                  const text = await resp.text();
                  return JSON.stringify({{
                    ok: true,
                    status: resp.status,
                    contentType: resp.headers.get('content-type'),
                    bodyHead: text.substring(0, 256),
                    redirected: resp.redirected,
                    finalUrl: resp.url
                  }});
                }} catch (err) {{
                  return JSON.stringify({{ ok: false, stage: stage, err: String(err) }});
                }}
              }})();";

            // NB: this Test is allowed to surface `EvaluationFailedException` — the
            // catch in the IIFE only fires for in-page errors. Page-context destruction
            // bubbles out of EvaluateExpressionAsync as a .NET exception.
            string result;
            try
            {
                result = await Subject.EvaluateRawDiagnostic(probe, CancellationToken.None);
            }
            catch (PuppeteerSharp.EvaluationFailedException ex)
            {
                Assert.Fail(
                    $"Probe D — fetch destroyed page context: {ex.Message}. " +
                    $"GAP-17-B isolated to the in-page fetch step (NOT decrypt). " +
                    $"Likely Cloudflare redirect or URL-prefix mismatch.");
                return;
            }

            // If we got here, fetch returned a response. Record what we saw.
            TestContext.Out.WriteLine("Probe D result: " + result);
            result.Should().Contain("\"ok\":true",
                "Probe D returned without page-destruction; record body shape for Plan 17-06 fix.");
        }

        [OneTimeTearDown]
        public void TearDownLiveSigner()
        {
            Subject?.Dispose();
        }
    }

    /// <summary>
    /// Diagnostic subclass exposing the cached probe expressions + EvaluateRawAsync seam.
    /// Lives in the test project, NOT production. Used ONLY by
    /// <see cref="ComixSignerStepwiseDiagnosticFixture"/>.
    /// </summary>
    public class DiagnosticHarnessSigner : ComixPuppeteerSigner
    {
        public DiagnosticHarnessSigner(NzbDrone.Core.Indexers.IIndexerSourceStatusService sourceStatusService, NLog.Logger logger)
            : base(sourceStatusService, logger)
        {
        }

        /// <summary>Captured signer namespace expression (e.g. `vmf_xyz.sig`); read from the
        /// `protected internal` accessor on the base class — NO reflection.</summary>
        public string SignerExprForDiagnostic => SignerExprForTest;

        /// <summary>Captured installer namespace expression; read from the `protected internal`
        /// accessor on the base class — NO reflection.</summary>
        public string InstallerExprForDiagnostic => InstallerExprForTest;

        /// <summary>Force the production warm-spawn path; cache probe exprs for diagnostic substitution.</summary>
        public async Task WarmAsync(CancellationToken ct)
        {
            // Trigger the production lazy-spawn + probe path. EvaluateRawAsync alone
            // bypasses the spawn — call ProxyFetchAsync once with a controlled apiPath
            // to force spawn + probe; swallow whatever it raises since the goal is the
            // warm page + cached signer/installer exprs, NOT the fetch result.
            try
            {
                await ProxyFetchAsync("/manga/__warmup__/chapters", ct);
            }
            catch (System.OperationCanceledException)
            {
                // WR-04 mitigation (Phase 17.2 WR-GC-04 piggyback fix per Plan 17.2-03):
                // cancellation is structural — never silently translate it into a "warm-up
                // succeeded with empty probe" state. Diagnostic-harness override must
                // preserve shutdown semantics so test cleanup / scheduler cancel surfaces
                // correctly to callers. Mirrors the canonical WR-04 pattern in
                // ComixIndexer.cs lines 170-178 (`catch (OperationCanceledException) { throw; }`).
                throw;
            }
            catch
            {
                // Probe + spawn is the goal; ignore non-cancellation fetch failures
                // (e.g. the controlled __warmup__ apiPath legitimately yields no chapter
                // data; we only need the warm-page + cached probe exprs as side-effect).
            }

            if (string.IsNullOrEmpty(SignerExprForTest) || string.IsNullOrEmpty(InstallerExprForTest))
            {
                throw new System.InvalidOperationException(
                    "Diagnostic harness could not capture signer/installer exprs after warm-up. " +
                    "GAP-17-B may be upstream of the IIFE; investigate probe-walk logic.");
            }
        }

        public Task<string> EvaluateRawDiagnostic(string js, CancellationToken ct)
            => EvaluateRawAsync(js, ct);
    }
}
