using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 33.1 (2026-05-25) regression lock for the env-module URL filter substring in
    /// <c>ComixPuppeteerSigner.EnsureEnvModuleAsync</c>. comix.to rotates its build bundle
    /// URLs roughly every 24-72h (Risk Register row 6); each rotation changes the env-module
    /// filename's per-build token, which breaks the <c>RequestFinished</c> filter and surfaces
    /// as a silent 30s timeout in the LIVE fixture. This Chromium-free grep-against-source lock
    /// turns the NEXT rotation into a build-time failure instead — the same role the Phase 17.2
    /// <c>UpstreamSignerDriftFixture</c> plays for the oracle architecture shape.
    ///
    /// <para>
    /// The 2026-05-25 rotation moved the env module from <c>env-tfgaak-*.js</c> to
    /// <c>env-tfkr3g-*.js</c>. See <c>.planning/debug/comix-signer-rotation-2026-05-25.md</c>
    /// for the LIVE investigation and <c>33.1-CONTEXT.md</c> "Investigation Outcome" for the
    /// confirmed hypothesis (H1) that surfaced the new substring.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixEnvModuleFilterFixture : CoreTest
    {
        private string _signerSource;

        [SetUp]
        public void Setup()
        {
            _signerSource = ReadCoreSource(
                TestContext.CurrentContext.TestDirectory,
                "src/NzbDrone.Core/Indexers/Comix/ComixPuppeteerSigner.cs");
        }

        private static string ReadCoreSource(string startDir, string relative)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException(
                $"Could not locate '{relative}' by walking up from '{startDir}'.");
        }

        [Test]
        public void Signer_must_filter_request_finished_for_current_env_module_pattern()
        {
            // Phase 33.1 (2026-05-25): the env module URL pattern rotated from
            // 'env-tfgaak-' to 'env-tfkr3g-'. Locking the new substring here so a
            // future rotation surfaces as a fixture failure before the LIVE fixture
            // burns 30s on each ProxyFetchAsync call.
            //
            // See .planning/debug/comix-signer-rotation-2026-05-25.md for the
            // investigation that surfaced the new pattern.
            _signerSource.Should().Contain(
                "env-tfkr3g-",
                "EnsureEnvModuleAsync must filter RequestFinished events for the current " +
                "env module URL pattern. If this assertion fails, comix.to may have " +
                "rotated the bundle URL pattern again — read the rotation log under " +
                ".planning/debug/comix-signer-rotation-*.md and re-run the Phase 33.1 " +
                "investigate-patch-verify loop with the new substring.");
        }

        [Test]
        public void Signer_must_not_re_introduce_retired_env_module_substring()
        {
            // The Phase 33.1 rotation-event marker comment IS allowed to reference the
            // retired 'env-tfgaak-' substring as documentation (single-quoted, inside a
            // // comment). What MUST NOT come back is the EXECUTABLE filter literal — the
            // double-quoted "env-tfgaak-" string used by url.IndexOf(...). Anchoring the
            // negative assertion on the double-quoted form lets the documentation comment
            // survive while still tripping if anyone reverts the filter literal.
            _signerSource.Should().NotMatchRegex(
                "\"env-tfgaak-\"",
                "Phase 33.1 retired the 'env-tfgaak-' substring on 2026-05-25. Re-introducing " +
                "the executable filter literal \"env-tfgaak-\" will re-surface the 30s env-module " +
                "capture timeout — see .planning/debug/comix-signer-rotation-2026-05-25.md.");
        }
    }
}
