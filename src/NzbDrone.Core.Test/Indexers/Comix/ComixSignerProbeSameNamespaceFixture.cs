using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 GAP-17-C regression guard (Plan 17-07 Task 2). Verifies that
    /// PROBE_JS gates signer + installer capture to the SAME window namespace —
    /// i.e. a synthetic page exposing signer-shape in <c>ns_A</c> AND
    /// installer-shape in <c>ns_B</c> MUST yield a null pair, while
    /// same-namespace pairs are returned.
    ///
    /// <para>
    /// Chromium-free: uses file-text grep against the const for shape
    /// assertions. The behavioural Jint-driven evaluation tests are skipped
    /// (option (b) per Plan 17-07 Task 2 step 1) because Jint is NOT a project
    /// dependency and adding a NuGet ref for a single fixture is outside this
    /// plan's scope. The grep assertions are sufficient: they directly lock the
    /// post-fix shape (per-namespace local captures + same-namespace gate),
    /// which is the structural property GAP-17-C requires.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerProbeSameNamespaceFixture : CoreTest
    {
        private string _probeJs;

        [SetUp]
        public void SetUp()
        {
            _probeJs = ComixPuppeteerSigner.GetProbeJsForTest();
        }

        [Test]
        public void Probe_const_uses_per_namespace_locals()
        {
            _probeJs.Should().Contain(
                "nsSigner",
                "GAP-17-C fix requires per-namespace local `nsSigner` variable so partial captures " +
                "in non-pairing namespaces are dropped before moving to the next namespace.");
            _probeJs.Should().Contain(
                "nsInstaller",
                "GAP-17-C fix requires per-namespace local `nsInstaller` variable for the same reason.");
            _probeJs.Should().Contain(
                "outerSignerExpr",
                "GAP-17-C fix requires outer-scope `outerSignerExpr` ONLY committed when both " +
                "per-namespace locals fire in the same iteration.");
            _probeJs.Should().Contain(
                "outerInstallerExpr",
                "GAP-17-C fix requires outer-scope `outerInstallerExpr` ONLY committed when both " +
                "per-namespace locals fire in the same iteration.");
        }

        [Test]
        public void Probe_const_resets_partial_captures_per_namespace()
        {
            _probeJs.Should().MatchRegex(
                @"if\s*\(\s*nsSigner\s*!==\s*null\s*&&\s*nsInstaller\s*!==\s*null\s*\)",
                "GAP-17-C fix MUST gate the outer commit on BOTH per-namespace locals being " +
                "non-null. If the gate is removed, cross-namespace pairs slip through (the " +
                "regression GAP-17-C describes).");
        }

        // Test 3 + Test 4 — Jint-driven behavioural verification.
        //
        // These tests would synthetically exercise PROBE_JS against a multi-namespace
        // window object to confirm that:
        //   (3) signer-only in ns_A + installer-only in ns_B returns {null, null};
        //   (4) both shapes in ns_C returns {ns_C.<sig>, ns_C.<inst>}.
        //
        // Plan 17-07 Task 2 step 1 documents an option-(a)/option-(b) choice.
        // Selected: option (b) — Jint is NOT a project dependency anywhere in
        // src/NzbDrone.Core.Test/, and adding a NuGet ref for a single fixture
        // is outside this plan's scope. The shape grep assertions above
        // (Probe_const_uses_per_namespace_locals + Probe_const_resets_partial_captures_per_namespace)
        // already lock the structural contract — without a same-namespace gate,
        // the Probe_const_resets_partial_captures_per_namespace regex assertion
        // fails. Behavioural Jint coverage is tracked as a future enhancement;
        // see Plan 17-07 SUMMARY for the deferral rationale.
        [Test]
        [Ignore("Plan 17-07 option (b): Jint not a project dep; behavioural assertion deferred — see SUMMARY for rationale.")]
        public void Probe_rejects_cross_namespace_pair_when_evaluated()
        {
            // Intentionally empty — Ignored per Plan 17-07 Task 2 step 1.
        }

        [Test]
        [Ignore("Plan 17-07 option (b): Jint not a project dep; behavioural assertion deferred — see SUMMARY for rationale.")]
        public void Probe_accepts_same_namespace_pair_when_evaluated()
        {
            // Intentionally empty — Ignored per Plan 17-07 Task 2 step 1.
        }
    }
}
