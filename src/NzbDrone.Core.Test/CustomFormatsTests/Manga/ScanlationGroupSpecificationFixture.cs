using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.CustomFormatsTests.Manga
{
    [TestFixture]
    public class ScanlationGroupSpecificationFixture : CoreTest<ScanlationGroupSpecification>
    {
        [Test]
        public void regex_matches_scanlation_group()
        {
            Subject.Value = "^Asura";
            var input = new MangaCustomFormatInput
            {
                Release = new ReleaseInfo { ScanlationGroup = "AsuraScans" }
            };
            Subject.IsSatisfiedBy(input).Should().BeTrue();
        }

        [Test]
        public void regex_does_not_match()
        {
            Subject.Value = "^Asura";
            var input = new MangaCustomFormatInput
            {
                Release = new ReleaseInfo { ScanlationGroup = "MangaPlus" }
            };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_input_is_not_MangaCustomFormatInput()
        {
            Subject.Value = "^Asura";
            var input = new CustomFormatInput();
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_scanlation_group_is_null()
        {
            Subject.Value = "^Asura";
            var input = new MangaCustomFormatInput
            {
                Release = new ReleaseInfo { ScanlationGroup = null }
            };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }

        [Test]
        public void does_not_match_when_release_is_null()
        {
            Subject.Value = "^Asura";
            var input = new MangaCustomFormatInput { Release = null };
            Subject.IsSatisfiedBy(input).Should().BeFalse();
        }
    }
}
