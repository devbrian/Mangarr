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

        // PARSE2-02 overflow guard (PR #271 Codex P1 + CodeRabbit Critical/Major): a
        // huge-but-parseable upper bound must NOT throw OverflowException. The cap is
        // enforced as `span < 1000*step` WITHOUT dividing the untrusted span, so neither
        // the `span/step` decimal division (which doubles span for the 0.5-grid case and
        // can exceed decimal.MaxValue) NOR the narrowing `(int)` cast can blow up. Both
        // the integer-grid (step=1) and fractional-grid (step=0.5, near-MaxValue) paths
        // fall through to per-chapter parsing instead of crashing on an untrusted title.
        [TestCase("Naruto Ch.1-3000000000", 1.0)]                                 // integer grid, > int.MaxValue span
        [TestCase("Naruto Ch.1.5-79000000000000000000000000000", 1.5)]            // 0.5 grid, span/0.5 > decimal.MaxValue
        public void Range_with_huge_parseable_bound_falls_through_without_throwing(string title, double expectedStart)
        {
            Action act = () => MangaParser.ParseChapterTitle(title);

            act.Should().NotThrow();

            var parsed = MangaParser.ParseChapterTitle(title);
            parsed.Should().NotBeNull();

            // Over-cap → range branch leaves ChapterNumbers empty → per-chapter
            // regex picks up the start as a single chapter (not a pathological expansion).
            parsed.ChapterNumbers.Should().Equal(new[] { (decimal)expectedStart });
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

        // debug session extras-academy-unknown-manga (2026-06-14): a marker WORD
        // ("Extra") embedded in the real title with a following apostrophe
        // ("Extra's" / "Extra’s") previously truncated MangaTitle to "The"
        // because the apostrophe supplied a `\b` boundary that let `extra\b` fire
        // mid-title. The truncated title never resolved to the library manga, so
        // every release was rejected as "Unknown Manga". The apostrophe guard keeps
        // the full title. Covers both the typographic (U+2019, as the gateway
        // returns it) and ASCII (U+0027) apostrophe.
        [TestCase("The Extra’s Academy Survival Guide - Chapter 42", "The Extra’s Academy Survival Guide")]
        [TestCase("The Extra's Academy Survival Guide - Chapter 42", "The Extra's Academy Survival Guide")]
        [TestCase("The Extra’s Academy Survival Guide Ch.42", "The Extra’s Academy Survival Guide")]
        public void MangaTitle_with_apostrophe_marker_word_is_not_truncated(string releaseTitle, string expectedTitle)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be(expectedTitle);
            parsed.ChapterNumbers.Should().Equal(new[] { 42m });
        }

        // Same guard on the ChapterType detector: a possessive marker word in the
        // title must NOT mis-classify a regular chapter as that type. Covers both the
        // typographic (U+2019, as the gateway returns it) and ASCII (U+0027) apostrophe.
        [TestCase("The Extra’s Academy Survival Guide - Chapter 42")]
        [TestCase("The Extra's Academy Survival Guide - Chapter 42")]
        public void ChapterType_is_not_misclassified_by_apostrophe_marker_word(string releaseTitle)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.ChapterType.Should().Be(ChapterType.Regular);
        }

        // Guard does NOT over-correct: legitimate trailing type markers (followed by
        // space/digit/dash/end, never an apostrophe) still strip from the title and
        // set the type.
        [Test]
        public void Legitimate_extra_marker_still_strips_and_sets_type()
        {
            var parsed = MangaParser.ParseChapterTitle("Some Manga - Extra 5");

            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be("Some Manga");
            parsed.ChapterType.Should().Be(ChapterType.Extra);
            parsed.ChapterNumbers.Should().Equal(new[] { 5m });
        }

        // debug session marker-word-title-truncation (2026-06-18): a type-marker word
        // ("Extra" / "Special") that is the legitimate TRAILING word of the title —
        // plain-space-preceded, no apostrophe, no following number — previously
        // truncated MangaTitle ("The Novel's Extra" → "The Novel's"; "A Returner's
        // Magic Should Be Special" → "A Returner's Magic Should Be") because the bare
        // `extra\b` / `special\b` alternation fired mid-title. The truncated title
        // never resolved to the library manga, so every release was rejected as
        // "Unknown Manga" (same class as extras-academy, but the apostrophe guard did
        // NOT cover this form). The two-arm fix only treats a type word as a delimiter
        // when it is dash-separated OR number-followed, so a trailing title type word
        // is preserved. A bare title with NO chapter number is intentionally NOT a
        // case here — ParseChapterTitle returns null for any release without a chapter
        // number or type marker (the validity gate in ParseChapterTitle), so realistic
        // release forms always carry a chapter token (`- Chapter 42`, `Ch.10`).
        [TestCase("The Novel's Extra - Chapter 42", "The Novel's Extra")]
        [TestCase("The Novel's Extra Ch.42", "The Novel's Extra")]
        [TestCase("A Returner's Magic Should Be Special - Chapter 10", "A Returner's Magic Should Be Special")]
        [TestCase("A Returner's Magic Should Be Special Ch.10", "A Returner's Magic Should Be Special")]
        public void MangaTitle_with_trailing_marker_word_is_not_truncated(string releaseTitle, string expectedTitle)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be(expectedTitle);
        }

        // Same fix on the ChapterType detector: a regular chapter of a title whose
        // last word is a type-marker word must NOT be classified as that type
        // (latent organizer-naming corruption, same class as extras-academy).
        [TestCase("The Novel's Extra Ch.42")]
        [TestCase("The Novel's Extra - Chapter 42")]
        [TestCase("A Returner's Magic Should Be Special Ch.10")]
        [TestCase("A Returner's Magic Should Be Special - Chapter 10")]
        public void ChapterType_is_not_misclassified_by_trailing_marker_word(string releaseTitle)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.ChapterType.Should().Be(ChapterType.Regular);
        }

        // Fix does NOT over-correct: legitimate type markers (dash-separated OR
        // number-followed) still strip from the title and set the type. Covers all
        // type words + both delimiter forms so the two-arm split is fully exercised.
        [TestCase("Some Manga - Extra 5", "Some Manga", ChapterType.Extra)]
        [TestCase("Some Manga Extra 18", "Some Manga", ChapterType.Extra)]
        [TestCase("Some Manga - Oneshot", "Some Manga", ChapterType.Oneshot)]
        [TestCase("Title - Side Story 3", "Title", ChapterType.SideStory)]
        [TestCase("Title - Prologue", "Title", ChapterType.Prologue)]
        [TestCase("Some Manga - Special 3", "Some Manga", ChapterType.Special)]
        [TestCase("Title Special 2", "Title", ChapterType.Special)]
        [TestCase("Some Manga - Bonus 1", "Some Manga", ChapterType.Bonus)]
        public void Legitimate_type_marker_still_strips_and_sets_type(string releaseTitle, string expectedTitle, ChapterType expectedType)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be(expectedTitle);
            parsed.ChapterType.Should().Be(expectedType);
        }

        // Chapter/volume tokens (vol|ch|chapter|…) are never plausible trailing title
        // words, so they remain dash-optional and strip at end-of-string — the
        // two-arm split must not regress them.
        [TestCase("Berserk Vol.5 Ch.42", "Berserk")]
        [TestCase("Naruto Chapter 100", "Naruto")]
        [TestCase("One Piece Ch.1050", "One Piece")]

        // PR #378 CodeRabbit review: the compact `c` token must match multi-digit and
        // decimal forms (c\d+(?:\.\d+)?), consistent with ChapterRegexes. The old `c\d`
        // left "One Piece c1050" un-stripped.
        [TestCase("One Piece c1050", "One Piece")]
        [TestCase("One Piece c14.5", "One Piece")]
        public void Chapter_volume_tokens_still_strip_dash_optional(string releaseTitle, string expectedTitle)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull();
            parsed.MangaTitle.Should().Be(expectedTitle);
        }

        // PR #378 Codex review regression guard: "oneshot" / "omake" are manga jargon,
        // never a legitimate trailing title word. A bracketed, numberless, dash-less
        // oneshot ("[Group] Chainsaw Man Oneshot (English)") must still classify as
        // ChapterType.Oneshot AND strip its title. Applying the strict dash-or-number
        // guard (correct for extra/special/…) to these reclassified them Regular and —
        // with no chapter number — made ParseChapterTitle return null, regressing real
        // oneshot imports. AlwaysMarker() restores the always-on jargon match.
        [TestCase("[Multi-Word Spaced Group] Chainsaw Man Oneshot (English)", "Chainsaw Man", ChapterType.Oneshot)]
        [TestCase("[Goldsleeves] Adachi to Shimamura Oneshot (ENG)", "Adachi to Shimamura", ChapterType.Oneshot)]
        [TestCase("Berserk Omake (EN)", "Berserk", ChapterType.Extra)]
        public void Numberless_jargon_marker_still_parses_and_sets_type(string releaseTitle, string expectedTitle, ChapterType expectedType)
        {
            var parsed = MangaParser.ParseChapterTitle(releaseTitle);

            parsed.Should().NotBeNull("jargon markers (oneshot/omake) are valid numberless releases");
            parsed.MangaTitle.Should().Be(expectedTitle);
            parsed.ChapterNumbers.Should().BeEmpty();
            parsed.ChapterType.Should().Be(expectedType);
        }
    }
}
