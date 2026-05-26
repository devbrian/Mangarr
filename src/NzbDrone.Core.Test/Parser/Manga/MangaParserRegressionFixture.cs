using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaParserTests
{
    /// <summary>
    /// Regression coverage for code-review-fix BL-03 + BL-04.
    ///
    /// BL-04: <c>DecimalSeparatorRegex</c> previously included <c>-</c> in its
    /// character class, so a multi-chapter range like <c>Ch.10-12</c> was collapsed
    /// to <c>Ch.10.12</c> BEFORE the multi-chapter-range regex (<c>ChapterRegexes[0]</c>)
    /// could match it. The fix restricts the normalizer to comma + underscore only,
    /// leaving the dash intact for the range regex.
    ///
    /// BL-03 (superseded by PARSE2-02): After BL-04's fix the range branch
    /// becomes live. It originally only enumerated integer ranges and let
    /// fractional bounds fall through. PARSE2-02 relaxes that guard to a fixed
    /// 0.5-step snap-to-grid expansion whenever either bound carries a fraction
    /// (e.g. <c>Ch.1-5.5</c> → <c>[1,1.5,2,2.5,3,3.5,4,4.5,5,5.5]</c>). Pure
    /// integer ranges still step by 1 (<c>Ch.10-12</c> → <c>[10,11,12]</c>).
    /// Descending bounds, oversized spans (count &gt; 1000), and off-grid
    /// endpoints that fall below the grid still fall through to per-chapter
    /// parsing so the range branch never blows up.
    /// </summary>
    [TestFixture]
    public class MangaParserRegressionFixture : CoreTest
    {
        // BL-04: integer multi-chapter range expands to the full integer set.
        [Test]
        public void Range_Ch10_12_expands_to_three_integer_chapters()
        {
            var parsed = MangaParser.ParseChapterTitle("Naruto Ch.10-12");

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().BeEquivalentTo(new[] { 10m, 11m, 12m });
        }

        // BL-04 supplementary: spaced range syntax also expands.
        [Test]
        public void Range_with_spaces_Ch10_to_12_still_expands()
        {
            var parsed = MangaParser.ParseChapterTitle("Bleach - Chapter 10 - 12");

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().BeEquivalentTo(new[] { 10m, 11m, 12m });
        }

        // PARSE2-02 Case 1 (headline fractional): a fractional upper bound
        // expands on the 0.5 grid. ROADMAP SC#2 verbatim target output.
        [Test]
        public void Range_with_fractional_upper_bound_expands_half_grid()
        {
            var parsed = MangaParser.ParseChapterTitle("Title Ch.1-5.5");

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().Equal(new[]
            {
                1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m, 5m, 5.5m
            });
        }

        // PARSE2-02 Case 2 (both fractional — REPURPOSED from the old
        // BL-03 fall-through test): a range with two fractional bounds expands
        // on the 0.5 grid starting from the (already-on-grid) lower bound.
        [Test]
        public void Range_with_fractional_bound_expands_on_half_grid()
        {
            var parsed = MangaParser.ParseChapterTitle("Title Ch.10.5-12.5");

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().Equal(new[] { 10.5m, 11m, 11.5m, 12m, 12.5m });
        }

        // PARSE2-02 Case 5 (off-grid endpoint snap): a non-.5 upper bound snaps
        // onto the grid, emitting grid points where value <= the bound (5.5 is
        // excluded because 5.5 > 5.3).
        [Test]
        public void Range_with_off_grid_endpoint_snaps_below_bound()
        {
            var parsed = MangaParser.ParseChapterTitle("Title Ch.1-5.3");

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().Equal(new[]
            {
                1m, 1.5m, 2m, 2.5m, 3m, 3.5m, 4m, 4.5m, 5m
            });
        }

        // PARSE2-02 Case 3 (descending — fall-through, DoS/sanity guard): a
        // descending range does NOT expand; it falls through to the per-chapter
        // regexes (which pick up the start as a single chapter). Asserted
        // defensively per RESEARCH §D because the per-chapter regex pickup is
        // ordering-sensitive.
        [Test]
        public void Range_descending_falls_through_to_per_chapter()
        {
            var parsed = MangaParser.ParseChapterTitle("Naruto Ch.12-10");

            parsed.Should().NotBeNull();

            // Descending range never expands; per-chapter regex picks up the start (12).
            parsed.ChapterNumbers.Should().Equal(new[] { 12m });
        }

        // PARSE2-02 Case 4 (oversized — fall-through, DoS expansion guard): a
        // range whose grid-point count exceeds the ~1000 cap does NOT expand
        // pathologically; it falls through to per-chapter parsing.
        [Test]
        public void Range_oversized_falls_through_without_expanding()
        {
            var parsed = MangaParser.ParseChapterTitle("Naruto Ch.1-2000");

            parsed.Should().NotBeNull();

            // Over-cap range never expands; per-chapter regex picks up the start (1).
            parsed.ChapterNumbers.Should().Equal(new[] { 1m });
        }

        // PARSE2-02 overflow guard (PR #271 Codex P1 / CodeRabbit Critical): a
        // huge-but-parseable upper bound must NOT throw OverflowException on the
        // decimal→int narrowing cast. The grid-step count is compared in decimal
        // against the cap BEFORE the cast, so an over-cap range falls through to
        // per-chapter parsing rather than crashing the parser on an untrusted title.
        [Test]
        public void Range_with_huge_parseable_bound_falls_through_without_throwing()
        {
            Action act = () => MangaParser.ParseChapterTitle("Naruto Ch.1-3000000000");

            act.Should().NotThrow();

            var parsed = MangaParser.ParseChapterTitle("Naruto Ch.1-3000000000");
            parsed.Should().NotBeNull();

            // Over-cap → range branch leaves ChapterNumbers empty → per-chapter
            // regex picks up the start (1) as a single chapter (not a 3e9-element expansion).
            parsed.ChapterNumbers.Should().Equal(new[] { 1m });
        }

        // BL-04 supplementary: decimal normalization for comma + underscore is
        // preserved (the dash exclusion did not break the comma path).
        [TestCase("Ch.14,5", 14.5)]
        [TestCase("Ch.14_5", 14.5)]
        public void Decimal_normalization_still_handles_comma_and_underscore(string input, double expected)
        {
            var parsed = MangaParser.ParseChapterTitle(input);

            parsed.Should().NotBeNull();
            parsed.ChapterNumbers.Should().HaveCount(1);
            parsed.ChapterNumbers[0].Should().Be((decimal)expected);
        }
    }
}
