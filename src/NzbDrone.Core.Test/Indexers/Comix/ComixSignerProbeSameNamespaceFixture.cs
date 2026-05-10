using System;
using System.IO;
using System.Linq;
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

        // Phase 17.2 D-1 (Mitigation A): settle the page execution context after PROBE_JS
        // so subsequent EvaluateExpressionAsync calls in EvaluateProxyFetchAsync hit a stable
        // context, not one mid-Runtime.executionContextDestroyed from PROBE_JS's pushState
        // side-effect (see 17-08-LIVE-VERIFICATION-EVIDENCE.md Finding 3, 17-LEARNINGS.md L-1/S-2).
        // Chromium-free regression guard: greps ComixPuppeteerSigner.cs source via the parent-walk
        // pattern from UpstreamSignerDriftFixture.ReadSignerSource — asserts a settle call appears
        // between the PROBE_JS evaluate line and the validation block, AND that the
        // "Phase 17.2 D-1" annotation marker is present so future maintainers don't
        // "simplify" the settle step away.
        [Test]
        public void Probe_must_settle_after_PROBE_JS_before_EvaluateProxyFetchAsync()
        {
            var src = ReadSignerSource(TestContext.CurrentContext.TestDirectory);

            src.Should().Contain(
                "Phase 17.2 D-1",
                "Phase 17.2 D-1 (Mitigation A): the annotation block above the new settle await " +
                "MUST remain so future maintainers don't 'simplify' it back to a bare " +
                "PROBE_JS-then-evaluate flow. The annotation cross-references " +
                "17-08-LIVE-VERIFICATION-EVIDENCE.md Finding 3 + 17-LEARNINGS.md L-1/S-2.");

            var probeIdx = src.IndexOf(
                "EvaluateExpressionAsync<ProbeResult>(PROBE_JS)",
                StringComparison.Ordinal);
            var validateIdx = src.IndexOf("if (probe == null", StringComparison.Ordinal);

            probeIdx.Should().BeGreaterThan(0,
                "PROBE_JS evaluate call must exist in LaunchAndProbeAsync — if this fails, " +
                "the file no longer contains the canonical PROBE_JS dispatch.");
            validateIdx.Should().BeGreaterThan(probeIdx,
                "the probe-result validation block (`if (probe == null ...)`) must follow " +
                "the PROBE_JS evaluate call — if this fails, LaunchAndProbeAsync has been " +
                "restructured in a way the regression guard does not recognize.");

            var betweenSlice = src.Substring(probeIdx, validateIdx - probeIdx);
            betweenSlice.Should().MatchRegex(
                @"WaitForNavigationAsync|WaitForNetworkIdleAsync|EvaluateExpressionAsync<bool>",
                "Phase 17.2 D-1 Mitigation A: a settle step (WaitForNavigationAsync / " +
                "WaitForNetworkIdleAsync / polling EvaluateExpressionAsync<bool>) MUST appear " +
                "between the PROBE_JS evaluate and the probe-result validation block, so the " +
                "next EvaluateExpressionAsync call (in EvaluateProxyFetchAsync) hits a stable " +
                "execution context — NOT one mid-Runtime.executionContextDestroyed from " +
                "PROBE_JS's pushState side-effect. See 17-08-LIVE-VERIFICATION-EVIDENCE.md " +
                "Finding 3 + 17-LEARNINGS.md L-1/S-2 for the falsification record.");
        }

        // Phase 17.2 follow-up regression guard: GetBakedChromiumPath fallback chain.
        //
        // Phase 17 shipped a Linux-Docker-only default ("/opt/mangarr-chromium") with no
        // platform-aware fallback; that worked in the production image but every Windows /
        // Mac / Linux non-Docker host returned null on the cache lookup, causing
        // PuppeteerSharp to fall back to its working-dir-relative `_output/net10.0/Chrome/...`
        // search and throw `ProcessException: Failed to launch browser` on first request.
        // Phase 17 didn't expose this because the live signer never worked end-to-end;
        // Phase 17.2's Mitigation A re-greened the live fixtures and surfaced it on the
        // first manual search through the running app. This guard locks the platform-aware
        // candidate list so a future maintainer can't "simplify" the fallback back to a
        // Linux-only default.
        [Test]
        public void Launcher_must_have_platform_aware_cache_fallback()
        {
            var candidates = ComixPuppeteerSigner.GetPlatformCacheCandidates().ToList();

            candidates.Should().Contain(
                "/opt/mangarr-chromium",
                "Phase 17 D-03 image-layer default MUST remain in the candidate list — " +
                "the production Docker image bakes Chromium under this exact path; removing " +
                "it would break the production launcher even though local-dev hosts would " +
                "still resolve via the platform-conventional candidates.");

            // The user / CI host either sets HOME (Linux/Mac) or USERPROFILE (Windows);
            // either way at least one of the home-derived candidates must appear.
            var xdgSegment = ".cache" + System.IO.Path.DirectorySeparatorChar + "mangarr-chromium";
            var hasHomeDerivedCandidate = candidates.Any(c =>
                c.Contains(xdgSegment, StringComparison.Ordinal) ||
                (c.Contains("mangarr-chromium", StringComparison.Ordinal) &&
                    (c.Contains("Library", StringComparison.Ordinal) ||
                     c.Contains("AppData", StringComparison.Ordinal) ||
                     c.Contains("Local", StringComparison.Ordinal))));

            hasHomeDerivedCandidate.Should().BeTrue(
                "the fallback chain MUST include at least one user-home-derived candidate " +
                "(XDG `~/.cache/mangarr-chromium`, Windows `%LOCALAPPDATA%\\mangarr-chromium`, " +
                "or macOS `~/Library/Caches/mangarr-chromium`) so local-dev workflows on " +
                "Windows/Mac/Linux non-Docker hosts don't require setting PUPPETEER_CACHE_DIR " +
                "before launching Mangarr. Actual candidates: " +
                string.Join(", ", candidates));
        }

        // Phase 17.2 follow-up: source-grep guard reinforcing the behavioral test above.
        // If GetPlatformCacheCandidates is removed (or its yields are gutted back to a single
        // /opt/mangarr-chromium return), this test fails with a verbatim diagnostic message
        // — even before the behavioral test catches the regression at runtime.
        [Test]
        public void Launcher_source_must_reference_platform_cache_candidates()
        {
            var src = ReadSignerSource(TestContext.CurrentContext.TestDirectory);

            src.Should().Contain(
                "GetPlatformCacheCandidates",
                "Phase 17.2 follow-up: GetBakedChromiumPath MUST delegate the cache-directory " +
                "candidate list to GetPlatformCacheCandidates so the platform-aware fallback " +
                "ordering is locked. If this fails, the launcher has been collapsed back to a " +
                "single hardcoded path — re-introducing the Phase 17 launcher gap that broke " +
                "Windows / Mac / Linux non-Docker hosts.");

            src.Should().Contain(
                "LOCALAPPDATA",
                "Phase 17.2 follow-up: the candidate list MUST honor the Windows %LOCALAPPDATA% " +
                "convention so Windows local-dev hosts resolve without operator-set env vars.");

            src.Should().Contain(
                "Library",
                "Phase 17.2 follow-up: the candidate list MUST honor the macOS " +
                "~/Library/Caches/mangarr-chromium convention.");
        }

        // Re-uses the parent-walk pattern from UpstreamSignerDriftFixture lines 53-70 verbatim
        // — climbs `dir.Parent` until `src/NzbDrone.Core/Indexers/Comix/ComixPuppeteerSigner.cs`
        // exists at any ancestor. Worktree-aware (L-9 from 17-LEARNINGS.md): worktree branches
        // place the test dir at a different depth than the canonical checkout, so a fixed-depth
        // climb breaks under .claude/worktrees/agent-* layouts.
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
    }
}
