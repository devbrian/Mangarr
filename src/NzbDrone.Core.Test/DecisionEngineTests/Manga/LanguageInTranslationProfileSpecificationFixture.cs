using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    [TestFixture]
    public class LanguageInTranslationProfileSpecificationFixture
        : MangaDecisionEngineSpecFixtureBase<LanguageInTranslationProfileSpecification>
    {
        [Test]
        public void accepts_when_release_language_in_profile()
        {
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en", "es" }));

            var rc = BuildRemoteChapter(releaseLanguage: "en", translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_release_language_not_in_profile_strict_mode()
        {
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en" }, allowOthers: false));

            var rc = BuildRemoteChapter(releaseLanguage: "ko", translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.LanguageNotInProfile);
        }

        [Test]
        public void accepts_when_release_language_not_in_profile_permissive_mode()
        {
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en" }, allowOthers: true));

            var rc = BuildRemoteChapter(releaseLanguage: "ko", translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_when_no_profile_assigned_and_no_global_default()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.DefaultTranslationProfileId)
                  .Returns((int?)null);

            var rc = BuildRemoteChapter(releaseLanguage: "ko", translationProfileId: null);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void rejects_when_release_has_no_TranslatedLanguage_and_strict_mode()
        {
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en" }, allowOthers: false));

            var rc = BuildRemoteChapter(releaseLanguage: null, translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.LanguageNotInProfile);
        }

        [Test]
        public void falls_back_to_global_default_when_per_Manga_FK_null()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(c => c.DefaultTranslationProfileId)
                  .Returns(99);
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(99))
                  .Returns(BuildTranslationProfile(99, new[] { "en" }, allowOthers: false));

            var rc = BuildRemoteChapter(releaseLanguage: "ko", translationProfileId: null);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.LanguageNotInProfile);
        }
    }
}
