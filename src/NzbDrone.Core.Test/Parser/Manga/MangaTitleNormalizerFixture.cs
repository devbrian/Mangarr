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
