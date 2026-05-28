using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Regression lock for the oracle-bundle URL filter in
    /// <c>ComixPlaywrightSigner.EnsureEnvModuleAsync</c>.
    ///
    /// <para>
    /// <b>Phase 33.3 (2026-05-25) — STRUCTURAL pivot.</b> comix.to rotates a per-build token across
    /// all bundle filenames roughly every 24-72h (Risk Register row 6; GH #266). The earlier locks
    /// chased that token (<c>env-tfgaak-</c> → <c>env-tfkr3g-</c>) and re-broke every rotation. The
    /// signer sniffs the STABLE structural prefix instead of the per-deploy token, so the token
    /// rotation no longer breaks capture.
    /// </para>
    /// <para>
    /// <b>2026-05-28 amendment (7th rotation event) — env↔manga OSCILLATION.</b> Phase 33.3 pinned
    /// only <c>…/dist/manga-</c> on the belief that the <c>env-*</c> bundle was permanently gone. The
    /// 2026-05-28 LIVE re-probe falsified that: the oracle bundle's BASE NAME oscillates between
    /// <c>env-</c> and <c>manga-</c> across rebuilds (env- 2026-05-23/25 → manga- 33.3 →
    /// <c>env-tfqu32-</c> 2026-05-28). The signer now matches EITHER stable structural prefix
    /// (<c>…/dist/env-</c> OR <c>…/dist/manga-</c>), so both the token rotation AND the env↔manga
    /// oscillation are no-ops. This lock therefore pins BOTH structural matches and FORBIDS
    /// re-introducing any per-build TOKEN literal (e.g. <c>env-tfqu32-</c>) in the executable filter —
    /// a token literal coming back is the rotation-treadmill anti-pattern these phases eliminated. The
    /// bare structural prefixes <c>env-</c>/<c>manga-</c> (no token suffix) are explicitly allowed.
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
                "src/NzbDrone.Core/Indexers/Comix/ComixPlaywrightSigner.cs");
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
        public void Signer_must_filter_request_finished_on_both_structural_bundle_prefixes()
        {
            // Phase 33.3 + 2026-05-28 amendment: the oracle bundle is matched STRUCTURALLY on EITHER
            // the '/dist/env-' OR the '/dist/manga-' prefix (the two base names comix.to oscillates the
            // oracle bundle between — stable across the per-deploy token rotation), NOT on a per-build
            // token literal. If this fails, comix.to may have renamed the oracle bundle off BOTH the
            // 'env-' and 'manga-' prefixes — re-run the LIVE investigation (see
            // .planning/debug/resolved/comix-signer-rotation-2026-05-28.md) to find the new structural
            // prefix; do NOT replace it with a per-build token (that re-creates the rotation treadmill
            // these phases removed).
            _signerSource.Should().MatchRegex(
                @"url\.IndexOf\(\s*""/dist/env-""\s*,\s*StringComparison\.OrdinalIgnoreCase\s*\)\s*>=\s*0",
                "EnsureEnvModuleAsync must filter RequestFinished events on the structural " +
                "url.IndexOf(\"/dist/env-\") prefix (one of the two stable oracle-bundle names).");
            _signerSource.Should().MatchRegex(
                @"url\.IndexOf\(\s*""/dist/manga-""\s*,\s*StringComparison\.OrdinalIgnoreCase\s*\)\s*>=\s*0",
                "EnsureEnvModuleAsync must filter RequestFinished events on the structural " +
                "url.IndexOf(\"/dist/manga-\") prefix (the other stable oracle-bundle name).");
        }

        [Test]
        public void Signer_must_not_re_introduce_a_per_build_token_filter_literal()
        {
            // The rotation-treadmill anti-pattern (GH #266): pinning the filter to a per-build token
            // (e.g. url.IndexOf("env-tfgaak-") / "env-tfkr3g-" / "env-tfqu32-" / any "<name>-<token>-")
            // re-breaks the signer on the next comix.to deploy. Rotation-event marker comments MAY
            // mention retired tokens as documentation; what must NOT return is an EXECUTABLE filter
            // literal that places a build TOKEN (alphanumerics) immediately after the 'env-'/'manga-'
            // base name. The bare structural prefixes "/dist/env-" and "/dist/manga-" (closing quote
            // right after the dash, no token) are the ALLOWED form and are asserted above.
            _signerSource.Should().NotMatchRegex(
                @"url\.IndexOf\(\s*""[^""]*(?:env|manga)-[A-Za-z0-9]",
                "Re-introducing an executable url.IndexOf(\"...env-<token>-\") / \"...manga-<token>-\" " +
                "filter (a build token immediately after the base name) re-creates the 24-72h rotation " +
                "treadmill (GH #266) — match the bare structural prefix instead.");
        }
    }
}
