using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests.Manga
{
    [TestFixture]
    public class SourceKeySpecificationFixture : CoreTest<SourceKeySpecification>
    {
        [Test]
        public void regex_matches_source_key()
        {
            Subject.Value = "mangadex";
            var input = new MangaCustomFormatInput { SourceKey = "mangadex" };
            Subject.IsSatisfiedBy(input).Should().BeTrue();
        }

        [Test]
        public void regex_does_not_match()
        {
            Subject.Value = "mangadex";
            var input = new MangaCustomFormatInput { SourceKey = "comix.to" };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_input_is_not_MangaCustomFormatInput()
        {
            Subject.Value = "mangadex";
            var input = new CustomFormatInput();
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_source_key_is_null()
        {
            Subject.Value = "mangadex";
            var input = new MangaCustomFormatInput { SourceKey = null };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void regex_alternation_matches_either_source()
        {
            Subject.Value = "^(mangadex|comix\\.to)$";
            var input = new MangaCustomFormatInput { SourceKey = "comix.to" };
            Subject.IsSatisfiedBy(input).Should().BeTrue();
        }
    }
}
