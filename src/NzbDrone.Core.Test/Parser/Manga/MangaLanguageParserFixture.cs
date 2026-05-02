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

        // WR-02 regression: spelled-out language names inside the leading scanlation
        // group bracket must NOT win. `[English Subbed]` is a group name, not a
        // language tag — the parser should ignore the leading bracket entirely when
        // scanning for spelled-out names.
        [TestCase("[English Subbed] Title - Ch.10", null)]
        [TestCase("[Engineer Translations] Title - Ch.10", null)]
        [TestCase("[Raw Time Scans] Title - Ch.10", null)]
        public void ParseLanguage_ignores_spelled_language_inside_leading_group_bracket(string input, string expected)
        {
            MangaLanguageParser.ParseLanguage(input).Should().Be(expected);
        }

        // WR-02 supplementary: spelled-out language elsewhere in the title still
        // wins (the strip only removes the FIRST bracket).
        [TestCase("[SomeGroup] Title - Ch.10 (English)", "en")]
        public void ParseLanguage_still_picks_up_spelled_language_outside_leading_bracket(string input, string expected)
        {
            MangaLanguageParser.ParseLanguage(input).Should().Be(expected);
        }
    }
}
