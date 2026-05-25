using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Regression lock for the oracle-bundle URL filter in
    /// <c>ComixPuppeteerSigner.EnsureEnvModuleAsync</c>.
    ///
    /// <para>
    /// <b>Phase 33.3 (2026-05-25) — STRUCTURAL pivot.</b> comix.to rotates a per-build token across
    /// all bundle filenames roughly every 24-72h (Risk Register row 6; GH #266). The earlier locks
    /// chased that token (<c>env-tfgaak-</c> → <c>env-tfkr3g-</c>) and re-broke every rotation. The
    /// signer now sniffs the STABLE structural prefix <c>…/dist/manga-</c> (the <c>env-*</c> bundle is
    /// gone; the decrypting path client lives in the <c>manga-*</c> chunk), so the per-deploy token
    /// rotation no longer breaks capture. This lock therefore pins the STRUCTURAL match and FORBIDS
    /// re-introducing any per-build token literal in the executable filter — a token literal coming
    /// back is the rotation-treadmill anti-pattern this phase eliminated.
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
        public void Signer_must_filter_request_finished_on_structural_manga_bundle_prefix()
        {
            // Phase 33.3: the oracle bundle is matched STRUCTURALLY on the '/dist/manga-' prefix
            // (stable across the per-deploy token rotation), NOT on a per-build token literal.
            // If this fails, comix.to may have renamed the oracle bundle off the 'manga-' prefix —
            // re-run the LIVE investigation (.planning/phases/33.3-.../33.3-RESEARCH.md) to find the
            // new structural prefix; do NOT replace it with a per-build token (that re-creates the
            // rotation treadmill this phase removed).
            _signerSource.Should().MatchRegex(
                @"url\.IndexOf\(\s*""/dist/manga-""\s*,\s*StringComparison\.OrdinalIgnoreCase\s*\)\s*>=\s*0",
                "EnsureEnvModuleAsync must filter RequestFinished events on the structural " +
                "url.IndexOf(\"/dist/manga-\") prefix (the stable oracle-bundle name), not a " +
                "rotating per-build token.");
        }

        [Test]
        public void Signer_must_not_re_introduce_a_per_build_token_filter_literal()
        {
            // The rotation-treadmill anti-pattern (GH #266): pinning the filter to a per-build token
            // (e.g. url.IndexOf("env-tfgaak-") / "env-tfkr3g-" / any "env-<token>-") re-breaks the
            // signer on the next comix.to deploy. Rotation-event marker comments MAY mention retired
            // tokens as documentation; what must NOT return is the EXECUTABLE filter literal
            // url.IndexOf("env-...").
            _signerSource.Should().NotMatchRegex(
                @"url\.IndexOf\(\s*""env-",
                "Phase 33.3 replaced per-build token filters with the structural '/dist/manga-' match. " +
                "Re-introducing an executable url.IndexOf(\"env-<token>-\") filter re-creates the " +
                "24-72h rotation treadmill (GH #266) — match the stable structural prefix instead.");
        }
    }
}
