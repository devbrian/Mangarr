using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 scaffold for CrossSourceIdResolver — D-21 Jaro-Winkler ≥ 0.85 +
    // 2-of-3 multi-axis confirmation gate. RED until Plan 02-09.
    //
    // [TestCase] rows below match VALIDATION.md row 2-RES-01 verbatim. The acceptance
    // criteria require these four [TestCase(...)] literals present in this file:
    //   [TestCase(0.85, 2, true,  ...)]   pass: 0.85 sim + 2 axes
    //   [TestCase(0.84, 2, false, ...)]   fail: 0.84 sim below the title gate
    //   [TestCase(0.85, 1, false, ...)]   fail: 0.85 sim + 1 axis below multi-axis gate
    //   [TestCase(0.85, 3, true,  ...)]   pass: 0.85 sim + 3 axes
    //
    // Plus per Pitfall 5: pure similarity reflexivity + dissimilarity sanity tests.
    [TestFixture]
    public class CrossSourceIdResolverFixture : CoreTest
    {
        [TestCase(0.85, 2, true,  "0.85 sim + 2 axes passes")]
        [TestCase(0.84, 2, false, "0.84 sim fails the title gate")]
        [TestCase(0.85, 1, false, "0.85 sim + 1 axis fails the multi-axis gate")]
        [TestCase(0.85, 3, true,  "0.85 sim + 3 axes passes")]
        [Ignore("RED — Plan 02-09 lands CrossSourceIdResolver.TryResolve.")]
        public void TryResolve_gate(double sim, int axes, bool expected, string scenario)
            => Assert.Inconclusive("Plan 02-09 — scenario: " + scenario);

        // Pitfall 5: F23.StringSimilarity Jaro-Winkler reflexivity sanity.
        [Test]
        [Ignore("RED — Plan 02-09 lands the Jaro-Winkler comparator.")]
        public void Similarity_naruto_to_naruto_is_one_point_zero()
            => Assert.Inconclusive("Plan 02-09");

        [Test]
        [Ignore("RED — Plan 02-09 lands the Jaro-Winkler comparator.")]
        public void Similarity_naruto_to_completely_different_is_below_threshold()
            => Assert.Inconclusive("Plan 02-09");
    }
}
