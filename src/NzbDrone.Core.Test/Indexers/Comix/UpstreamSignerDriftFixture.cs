using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 W-4 (revision iteration 1): Chromium-free drift falsifier.
    /// Compares the planned PROBE_JS const + proxyFetch JS shape in
    /// <c>ComixPuppeteerSigner.cs</c> against the captured upstream excerpt at
    /// <c>Resources/upstream-signer.txt</c>. If the SHA-pinned upstream changes
    /// its PROBE_JS shape (e.g. abandons the <c>vmf_*</c> namespace prefix), this
    /// fixture FAILS — the executor knows to re-port BEFORE the live test in Wave 3.
    ///
    /// <para>
    /// Wave 0 leaves this fixture <c>[Ignore]</c>'d at class level because the
    /// planned <c>ComixPuppeteerSigner.cs</c> file does not exist yet. Wave 1
    /// (Plan 17-02 Task 1b) authors that file and un-Ignores this fixture.
    /// </para>
    ///
    /// <para>
    /// The upstream-signer.txt resource itself is verified non-empty +
    /// vmf_-bearing by the build-time grep acceptance criterion in Plan 17-01
    /// Task 4 verify; this fixture is the runtime cross-check.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UpstreamSignerDriftFixture : CoreTest
    {
        private string _upstreamExcerpt;
        private string _signerSource;

        [SetUp]
        public void Setup()
        {
            // Resource shipped via csproj <None Update="..." CopyToOutputDirectory>.
            var resourcePath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Indexers",
                "Comix",
                "Resources",
                "upstream-signer.txt");
            _upstreamExcerpt = File.ReadAllText(resourcePath);

            // ComixPuppeteerSigner.cs lives in NzbDrone.Core. The Wave 0 fixture climbed
            // a fixed number of `..`s from `_tests/net10.0/`, which breaks in worktree
            // layouts (the repo root may not be 2-or-4 levels up from the test dir).
            // Walk parent directories until we find `src/NzbDrone.Core/Indexers/Comix/ComixPuppeteerSigner.cs`.
            _signerSource = ReadSignerSource(TestContext.CurrentContext.TestDirectory);
        }

        private static string ReadSignerSource(string startDir)
        {
            const string Relative = "src/NzbDrone.Core/Indexers/Comix/ComixPuppeteerSigner.cs";
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, Relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                $"Could not locate ComixPuppeteerSigner.cs by walking up from '{startDir}'.");
        }

        [Test]
        public void PROBE_JS_should_reference_vmf_namespace_prefix_per_upstream()
        {
            _upstreamExcerpt.Should().Contain("vmf_",
                "upstream Signer.kt is expected to use the vmf_* window-namespace prefix; " +
                "if upstream drifted, capture a fresh excerpt at port-time");
            _signerSource.Should().Contain("vmf_",
                "ComixPuppeteerSigner.PROBE_JS must reference vmf_* per upstream — " +
                "if planner removed it, executor must verify against fresh upstream capture");
        }

        [Test]
        public void Planned_proxyFetch_template_should_reference_response_interceptor_shape()
        {
            _upstreamExcerpt.Should().Contain("interceptors",
                "upstream Signer.kt is expected to install a response interceptor; " +
                "if upstream changed shape, capture fresh excerpt");
            _signerSource.Should().Contain("interceptors",
                "ComixPuppeteerSigner.EvaluateProxyFetchAsync JS template must mirror upstream's interceptor shape");
        }

        [Test]
        public void Port_must_capture_response_interceptor_not_no_op_it()
        {
            // CR-01 regression guard (revision iteration 2): the Wave 1 implementation
            // initially wired `interceptors.response.use(() => {})` — a no-op that
            // silently dropped the upstream-registered decrypt function. The above
            // substring assertion ("interceptors" present) was too weak to catch this.
            //
            // The corrected port mirrors upstream's `use: function(fn) { captured.res = fn; }`
            // shape (Resources/upstream-signer.txt:84). Assert the SHAPE — a `captured`
            // variable is closed over, AND the response.use callback BODY assigns into
            // captured.res (not an empty body).
            _signerSource.Should().Contain("captured",
                "EvaluateProxyFetchAsync template must close over a `captured` object holding " +
                "the request + response interceptors registered by installer() — per " +
                "upstream Signer.kt:80-84. A `() => {}` no-op silently drops the decrypt fn.");
            _signerSource.Should().Contain("captured.res",
                "EvaluateProxyFetchAsync template must assign the response interceptor into " +
                "`captured.res` so the decrypt function can be invoked on encrypted bodies.");

            // Note: the production source's JS template lives inside a `$@""` interpolated
            // verbatim string — literal braces are doubled (`{{` and `}}`). Match against
            // the doubled-brace shape that actually appears in the .cs file.
            _signerSource.Should().MatchRegex(
                @"response\s*:\s*\{\{\s*use\s*:\s*function\s*\(\s*fn\s*\)\s*\{\{\s*captured\.res\s*=\s*fn\s*;",
                "EvaluateProxyFetchAsync's fake-axios `interceptors.response.use` MUST capture " +
                "the registered fn into a closure variable, NOT be a `() => {}` no-op. The no-op " +
                "shape silently drops the upstream decrypt function and ships encrypted bodies.");
        }

        [Test]
        public void Port_must_invoke_captured_response_interceptor_on_encrypted_body()
        {
            // CR-01 regression guard: capturing `captured.res` is necessary but not
            // sufficient — the template must also INVOKE it on the encrypted-body shape.
            // Upstream's shape: detect `'e' in raw && captured.res`, build a fakeResp,
            // `await captured.res(fakeResp)`, return `decoded.data` (Resources/upstream-signer.txt:101-110).
            _signerSource.Should().Contain("'e' in raw",
                "EvaluateProxyFetchAsync must detect the encrypted-body envelope (`{e: ...}`) " +
                "before invoking the captured response interceptor — per upstream Signer.kt:101.");
            _signerSource.Should().Contain("captured.res(",
                "EvaluateProxyFetchAsync must INVOKE the captured response interceptor on the " +
                "encrypted body — not just capture-and-discard. Upstream calls `await captured.res(fakeResp)`.");
        }

        [Test]
        public void Port_must_sign_path_without_query_string()
        {
            // CR-02 regression guard (revision iteration 2): the Wave 1 implementation
            // initially called `signer(apiPath)` — passing the FULL path including the
            // chapter-list `?order[number]=desc&limit=...&mangaSlug=...` query string.
            // Per Resources/upstream-signer.txt:124-125, upstream signs
            // `apiPath.substringBefore('?')` — the path WITHOUT the query string.
            //
            // Mismatched signer input → wrong token → comix.to backend rejects every
            // chapter-list request with 403. The "interceptors substring present"
            // assertion above did not catch this; this regex enforces the query-strip.
            _signerSource.Should().MatchRegex(
                @"signablePath\s*=\s*apiPath\.split\('\?'\)\[0\]",
                "EvaluateProxyFetchAsync's JS template must strip the query string from apiPath " +
                "before passing it to signer() — per upstream Signer.kt:124-125 " +
                "(`apiPath.substringBefore('?')`). Signing the full path-with-query produces a " +
                "token comix.to rejects.");
            _signerSource.Should().Contain("signer(signablePath)",
                "EvaluateProxyFetchAsync must pass the query-stripped `signablePath` (NOT the raw " +
                "apiPath) into the upstream signer function.");
        }

        [Test]
        public void Planned_decryptedBody_shim_should_match_upstream_response_handling()
        {
            // The window.__decryptedBody__ shim is the planner's best-effort port. If
            // the upstream excerpt reveals a different shim name, the fixture fails
            // and the live test in Wave 3 is bypassed (cheaper failure mode).
            var hasShim = _upstreamExcerpt.Contains("__decryptedBody__")
                       || _upstreamExcerpt.Contains("response.data")
                       || _upstreamExcerpt.Contains("decoded.data");
            hasShim.Should().BeTrue(
                "upstream Signer.kt is expected to expose decoded body via either " +
                "window.__decryptedBody__ shim OR response.data interceptor; " +
                "if neither, re-port required");
        }

        [Test]
        public void Captured_upstream_SHA_must_be_recorded_in_resource_header()
        {
            // T-17-01-05 mitigation: the resource is committed to repo at port-time
            // SHA. If anyone refreshes the excerpt without updating the SHA header,
            // we lose the pinning contract — this assertion catches that.
            _upstreamExcerpt.Should().Contain("Upstream commit SHA:",
                "Resources/upstream-signer.txt MUST carry a verbatim 'Upstream commit SHA:' header line");
        }

        [Test]
        public void Port_must_not_regress_GAP_17_B_decrypt_guard()
        {
            // GAP-17-B Branch C regression guard (Plan 17-06): Plan 17-05's Probe D
            // diagnosis localized the cause of `EvaluationFailedException: Execution
            // context was destroyed` to the in-page `await captured.res(fakeResp)`
            // decrypt invocation — Probe D omits that step and returns 200 OK +
            // `{"e":"..."}` cleanly; the production path WITH the decrypt step
            // reliably fires the lazy-reprobe Warn on every WarmAsync.
            //
            // Plan 17-06 Branch C wrapped the decrypt invocation in an in-page
            // try/catch that surfaces a `decryptError` envelope on throw instead
            // of letting the rejection tear down the page. Removing the guard
            // regresses GAP-17-B (the live fixture transitions back from passing
            // to throwing EvaluationFailedException on every call).
            _signerSource.Should().Contain("decryptError",
                "Plan 17-06 Branch C wrapped `await captured.res(fakeResp)` in an " +
                "in-page try/catch and surfaces the encrypted shape via a `decryptError` " +
                "envelope on throw. Removing the guard regresses GAP-17-B — the " +
                "decrypt interceptor's window mutation tears down the warm page on " +
                "every call.");

            _signerSource.Should().Contain("GAP-17-B Branch C",
                "Branch C annotation block above EvaluateProxyFetchAsync must remain so " +
                "future maintainers don't 'simplify' the catch back to a bare await — " +
                "removing the annotation suggests the rationale has been lost.");
        }
    }
}
