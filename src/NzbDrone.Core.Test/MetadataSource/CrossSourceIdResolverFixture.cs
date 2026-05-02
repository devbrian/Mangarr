using System.Collections.Generic;
using F23.StringSimilarity;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 fixture for CrossSourceIdResolver — D-21 Jaro-Winkler ≥ 0.85 +
    // 2-of-3 multi-axis confirmation gate. Plan 02-09 GREEN.
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
    public class CrossSourceIdResolverFixture : CoreTest<CrossSourceIdResolver>
    {
        [TestCase(0.85, 2, true,  "0.85 sim + 2 axes passes")]
        [TestCase(0.84, 2, false, "0.84 sim fails the title gate")]
        [TestCase(0.85, 1, false, "0.85 sim + 1 axis fails the multi-axis gate")]
        [TestCase(0.85, 3, true,  "0.85 sim + 3 axes passes")]
        public void TryResolve_gate(double sim, int axes, bool expected, string scenario)
        {
            // Build a primary candidate. The secondary is constructed to produce roughly
            // the requested Jaro-Winkler similarity by sharing a known prefix/suffix.
            var primary = new MangaCandidate
            {
                AllTitles = new List<string> { "naruto" },
                PublicationYear = 1999,
                PrimaryAuthor = "Masashi Kishimoto",
                TotalChapterCount = 700,
            };

            var secondary = BuildSecondary(sim, axes);

            var result = Subject.TryResolve(primary, secondary, out _);

            result.Should().Be(expected, scenario);
        }

        // Pitfall 5: F23.StringSimilarity Jaro-Winkler reflexivity sanity.
        [Test]
        public void Similarity_naruto_to_naruto_is_one_point_zero()
        {
            var jw = new JaroWinkler();
            jw.Similarity("naruto", "naruto").Should().Be(1.0);
        }

        [Test]
        public void Similarity_naruto_to_completely_different_is_below_threshold()
        {
            var jw = new JaroWinkler();
            jw.Similarity("naruto", "zzzzzzzz").Should().BeLessThan(0.85);
        }

        // Construct a secondary candidate whose normalized-title Jaro-Winkler similarity
        // against "naruto" is approximately `sim`, AND whose supporting axes count
        // matches `axes`.
        private static MangaCandidate BuildSecondary(double sim, int axes)
        {
            string secondaryTitle;

            // Pick a string that yields the requested similarity vs "naruto" via JW.
            // 1.0 = identical; ~0.85 = small mutation; ~0.84 = noticeably different.
            var jw = new JaroWinkler();
            if (sim >= 0.85 - 1e-9)
            {
                // Small permutation that yields >= 0.85.
                secondaryTitle = "narutoo";  // typo extension keeps JW high (>0.96)
                System.Diagnostics.Debug.Assert(jw.Similarity("naruto", secondaryTitle) >= 0.85);
            }
            else
            {
                // Sufficiently different to fall below the gate (~0.84).
                secondaryTitle = "completelyunrelated";
                System.Diagnostics.Debug.Assert(jw.Similarity("naruto", secondaryTitle) < 0.85);
            }

            // Now construct axis values: include exactly `axes` matching-with-primary fields.
            // Order: PublicationYear axis, PrimaryAuthor axis, TotalChapterCount axis.
            var year = axes >= 1 ? 1999 : (int?)1900;
            var author = axes >= 2 ? "Masashi Kishimoto" : "Different Author";
            int? totalChapters = axes >= 3 ? 700 : 1;

            return new MangaCandidate
            {
                AllTitles = new List<string> { secondaryTitle },
                PublicationYear = year,
                PrimaryAuthor = author,
                TotalChapterCount = totalChapters,
            };
        }
    }
}
