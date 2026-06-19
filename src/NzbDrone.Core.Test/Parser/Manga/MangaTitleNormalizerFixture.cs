using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaParserTests
{
    /// <summary>
    /// Title canonicalization (D-05). Single source of truth for AddManga dedup +
    /// CrossSourceIdResolver. RED until Plan 02-04.
    /// </summary>
    [TestFixture]
    public class MangaTitleNormalizerFixture : CoreTest
    {
        [TestCase("Demon Slayer (Manga)", "demon slayer")]
        [TestCase("Ｄｅｍｏｎ Ｓｌａｙｅｒ", "demon slayer")]
        [TestCase("Café au lait", "cafe au lait")]
        [TestCase("My/Hero!Academia?", "myheroacademia")]
        [TestCase("鬼滅の刃", "鬼滅の刃")]
        public void Normalize_canonicalizes(string input, string expected)
        {
            MangaTitleNormalizer.Normalize(input).Should().Be(expected);
        }

        // debug `alt-title-collision-guard` (2026-06-19): junk placeholder titles carry zero
        // identification value and must never be used as a resolution key (write-time they are
        // dropped from AlternativeTitles; read-time GetManga refuses to resolve them). EXACT-match
        // denylist only — legit short aliases ("orv", "trk", "mga") and the garbage-but-not-
        // -placeholder word "but" (handled by the FindByAlternativeTitle ambiguity guard) are NOT
        // flagged. Input is normalized defensively so raw titles work too.
        [TestCase("Unknown Title", true)]
        [TestCase("unknown title", true)]
        [TestCase("UNKNOWN TITLE", true)]
        [TestCase("Title Unknown", true)]
        [TestCase("No Title", true)]
        [TestCase("Untitled", true)]
        [TestCase("unknown", true)]
        [TestCase("None", true)]
        [TestCase("TBA", true)]
        [TestCase("TBD", true)]
        [TestCase("", true)]
        [TestCase("   ", true)]
        [TestCase(null, true)]
        [TestCase("Attack on Titan", false)]
        [TestCase("orv", false)]      // legit abbreviation — Omniscient Reader
        [TestCase("trk", false)]      // legit abbreviation — Tomb Raider King
        [TestCase("mga", false)]      // legit abbreviation — Martial God Asura
        [TestCase("but", false)]      // garbage word, NOT a known placeholder (ambiguity guard covers the shared case)
        [TestCase("Naruto", false)]
        public void IsJunkPlaceholder_flags_only_known_placeholders(string input, bool expected)
        {
            MangaTitleNormalizer.IsJunkPlaceholder(input).Should().Be(expected);
        }

        // Search-query variant: identical pipeline to Normalize EXCEPT stripped
        // punctuation becomes a SPACE so word boundaries survive for an external
        // tokenizing search engine. Regression coverage for debug session
        // chick-class-hunter-search-miss (2026-06-07): the hyphen in "Chick-Class"
        // must NOT merge the two words into the non-existent token "chickclass".
        [TestCase("Chick-Class Hunter", "chick class hunter")]
        [TestCase("My/Hero!Academia?", "my hero academia")]
        [TestCase("Re:Zero", "re zero")]
        [TestCase("Demon Slayer (Manga)", "demon slayer")]
        [TestCase("Ｄｅｍｏｎ Ｓｌａｙｅｒ", "demon slayer")]
        [TestCase("Café au lait", "cafe au lait")]
        [TestCase("鬼滅の刃", "鬼滅の刃")]
        public void NormalizeForSearch_preserves_word_boundaries(string input, string expected)
        {
            MangaTitleNormalizer.NormalizeForSearch(input).Should().Be(expected);
        }

        // The comparison form is unchanged: it still merges across punctuation so
        // symmetric in-DB matching (dedup / FindByAlternativeTitle) is unaffected
        // by the search-query fix.
        [TestCase("Chick-Class Hunter", "chickclass hunter")]
        public void Normalize_still_merges_across_punctuation(string input, string expected)
        {
            MangaTitleNormalizer.Normalize(input).Should().Be(expected);
        }
    }
}
