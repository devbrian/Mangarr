using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests.Manga
{
    [TestFixture]
    public class TranslatedLanguageSpecificationFixture : CoreTest<TranslatedLanguageSpecification>
    {
        [Test]
        public void matches_when_release_translated_language_equals_value_case_insensitive()
        {
            Subject.Value = "en";
            var input = new MangaCustomFormatInput
            {
                Release = new ReleaseInfo { TranslatedLanguage = "EN" }
            };
            Subject.IsSatisfiedBy(input).Should().BeTrue();
        }

        [Test]
        public void does_not_match_when_release_translated_language_differs()
        {
            Subject.Value = "en";
            var input = new MangaCustomFormatInput
            {
                Release = new ReleaseInfo { TranslatedLanguage = "ko" }
            };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_input_is_not_MangaCustomFormatInput()
        {
            Subject.Value = "en";
            var input = new CustomFormatInput();
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_release_is_null()
        {
            Subject.Value = "en";
            var input = new MangaCustomFormatInput { Release = null };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void validate_rejects_invalid_BCP47()
        {
            Subject.Value = "zzz999not-a-code";
            var result = Subject.Validate();
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void validate_accepts_valid_BCP47_two_letter()
        {
            Subject.Value = "en";
            var result = Subject.Validate();
            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void validate_rejects_empty_value()
        {
            Subject.Value = "";
            var result = Subject.Validate();
            result.IsValid.Should().BeFalse();
        }
    }
}
