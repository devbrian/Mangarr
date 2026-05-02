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
    }
}
