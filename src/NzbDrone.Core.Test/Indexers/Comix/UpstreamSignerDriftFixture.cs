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
    }
}
