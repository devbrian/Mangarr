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
    /// Phase 17.2 follow-up regression guard, preserved across the 2026-05-22
    /// captureToken rewrite of <see cref="ComixPuppeteerSigner"/>.
    ///
    /// <para>
    /// Phase 17 shipped a Linux-Docker-only default (<c>/opt/mangarr-chromium</c>) with no
    /// platform-aware fallback; that worked in the production image but every Windows /
    /// Mac / Linux non-Docker host returned null on the cache lookup, causing
    /// PuppeteerSharp to fall back to its working-dir-relative
    /// <c>_output/net10.0/Chrome/...</c> search and throw
    /// <c>ProcessException: Failed to launch browser</c> on first request. Phase 17 didn't
    /// expose this because the live signer never worked end-to-end; Phase 17.2's
    /// Mitigation A re-greened the live fixtures and surfaced it on the first manual
    /// search through the running app. This guard locks the platform-aware candidate
    /// list so a future maintainer can't "simplify" the fallback back to a Linux-only
    /// default.
    /// </para>
    ///
    /// <para>
    /// Originally lived in <c>ComixSignerProbeSameNamespaceFixture</c>; moved into its
    /// own fixture when PROBE_JS was retired (2026-05-22 captureToken rewrite — see
    /// <c>.planning/debug/comix-signer-rotation.md</c>).
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerPlatformCacheFallbackFixture : CoreTest
    {
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

        // Phase 17.2 follow-up: source-grep guard reinforcing the behavioural test above.
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

        // Worktree-aware parent-walk pattern (L-9 from 17-LEARNINGS.md): worktree branches
        // place the test dir at a different depth than the canonical checkout, so a
        // fixed-depth climb breaks under .claude/worktrees/agent-* layouts.
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
