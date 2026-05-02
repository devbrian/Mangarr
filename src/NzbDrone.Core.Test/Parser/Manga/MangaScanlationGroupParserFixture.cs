using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaParserTests
{
    /// <summary>
    /// Scanlation-group extraction from manga release titles (D-04). Mirrors
    /// AnimeReleaseGroupRegex (<c>^[Group]</c>). RED until Plan 02-04.
    /// </summary>
    [TestFixture]
    public class MangaScanlationGroupParserFixture : CoreTest
    {
        [TestCase("[Mangastream] Naruto - Ch.700", "Mangastream")]
        [TestCase("[Multi-word Group] Title - Ch.10", "Multi-word Group")]
        [TestCase("Title - Ch.10", null)]
        public void ParseScanlationGroup_extracts(string input, string expected)
        {
            MangaScanlationGroupParser.ParseScanlationGroup(input).Should().Be(expected);
        }
    }
}
