using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.Profiles.CustomFormats;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    [TestFixture]
    public class CustomFormatMinimumScoreSpecificationFixture
        : MangaDecisionEngineSpecFixtureBase<CustomFormatMinimumScoreSpecification>
    {
        [Test]
        public void accepts_when_score_within_bounds()
        {
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildCustomFormatProfile(7, min: 0, max: null));

            var rc = BuildRemoteChapter(customFormatScore: 50, customFormatProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_score_below_min()
        {
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildCustomFormatProfile(7, min: 100, max: null));

            var rc = BuildRemoteChapter(customFormatScore: 50, customFormatProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.CustomFormatMinimumScore);
        }

        [Test]
        public void rejects_when_score_above_max()
        {
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildCustomFormatProfile(7, min: 0, max: 100));

            var rc = BuildRemoteChapter(customFormatScore: 200, customFormatProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.CustomFormatMaximumScore);
        }

        [Test]
        public void accepts_when_max_is_null()
        {
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildCustomFormatProfile(7, min: 0, max: null));

            var rc = BuildRemoteChapter(customFormatScore: 999_999, customFormatProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        // Regression: a profile saved with the max field left blank persists MaxFormatScore = 0,
        // NOT null. A literal 0 must be treated as "no cap" — otherwise every release with any
        // positive CF score is rejected ("score 25 exceeds profile 'X' maximum 0").
        [Test]
        public void accepts_when_max_is_zero_treated_as_no_cap()
        {
            Mocker.GetMock<ICustomFormatProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildCustomFormatProfile(7, min: 0, max: 0));

            var rc = BuildRemoteChapter(customFormatScore: 25, customFormatProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_when_no_profile_assigned_and_no_global_default()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.DefaultCustomFormatProfileId)
                  .Returns((int?)null);

            var rc = BuildRemoteChapter(customFormatScore: 50, customFormatProfileId: null);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }
    }
}
