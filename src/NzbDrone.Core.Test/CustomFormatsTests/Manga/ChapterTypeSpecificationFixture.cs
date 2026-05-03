using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests.Manga
{
    [TestFixture]
    public class ChapterTypeSpecificationFixture : CoreTest<ChapterTypeSpecification>
    {
        [Test]
        public void matches_when_chapter_type_equals_value()
        {
            Subject.Value = (int)ChapterType.Extra;
            var input = new MangaCustomFormatInput
            {
                ChapterInfo = new ParsedChapterInfo { ChapterType = ChapterType.Extra }
            };
            Subject.IsSatisfiedBy(input).Should().BeTrue();
        }

        [Test]
        public void does_not_match_when_chapter_type_differs()
        {
            Subject.Value = (int)ChapterType.Extra;
            var input = new MangaCustomFormatInput
            {
                ChapterInfo = new ParsedChapterInfo { ChapterType = ChapterType.Regular }
            };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_input_is_not_MangaCustomFormatInput()
        {
            Subject.Value = (int)ChapterType.Extra;
            var input = new CustomFormatInput();
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_chapter_info_is_null()
        {
            Subject.Value = (int)ChapterType.Extra;
            var input = new MangaCustomFormatInput { ChapterInfo = null };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void validate_rejects_out_of_range_enum_value()
        {
            Subject.Value = 999;
            var result = Subject.Validate();
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void validate_accepts_valid_enum_value()
        {
            Subject.Value = (int)ChapterType.Oneshot;
            var result = Subject.Validate();
            result.IsValid.Should().BeTrue();
        }
    }
}
