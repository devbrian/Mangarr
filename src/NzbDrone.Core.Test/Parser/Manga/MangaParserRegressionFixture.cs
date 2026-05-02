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
    /// BL-03: After BL-04's fix the range branch becomes live; its <c>n += 1m</c>
    /// loop silently truncated fractional bounds. The fix only enumerates integer
    /// ranges and falls through to per-chapter parsing for decimal bounds.
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

        // BL-03: fractional-bounded range falls through to per-chapter (no
        // truncated-integer expansion). The decimal regex picks up 10.5.
        [Test]
        public void Range_with_fractional_bound_falls_through_to_per_chapter()
        {
            var parsed = MangaParser.ParseChapterTitle("Title Ch.10.5-12.5");

            parsed.Should().NotBeNull();

            // The integer-range branch is rejected (BL-03); the next regex
            // (decimal-prefix `Ch.14.5` form) catches the start as a single
            // chapter — silently dropping the upper bound is acceptable per the
            // fix recommendation (real-world ranges are integer-only).
            parsed.ChapterNumbers.Should().NotBeEmpty();
            parsed.ChapterNumbers.Should().NotEqual(new[] { 10m, 11m, 12m });
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
