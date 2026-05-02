using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaParserTests
{
    /// <summary>
    /// BCP-47 translated-language extraction from manga release titles (D-04, LANG-01).
    /// RED until Plan 02-04.
    /// </summary>
    [TestFixture]
    public class MangaLanguageParserFixture : CoreTest
    {
        [TestCase("[Group] Title - Ch.10 [EN]", "en")]
        [TestCase("[Grupo] Title - Ch.10 (Spanish)", "es")]
        [TestCase("[Group] Title - Ch.10 [ja]", "ja")]
        [TestCase("[Group] Title - Ch.10", null)]
        public void ParseLanguage_extracts_bcp47(string input, string expected)
        {
            MangaLanguageParser.ParseLanguage(input).Should().Be(expected);
        }
    }
}
