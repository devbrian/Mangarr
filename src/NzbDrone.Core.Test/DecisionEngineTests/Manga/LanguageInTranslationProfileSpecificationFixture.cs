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

        // Sonarr divergence: Phase 16 STRUCT-06 verification + multi-release regression guard.
        // Per RESEARCH.md §"Pattern: LanguageInTranslationProfileSpecification — verification only",
        // this spec already operates at release-grain (`subject.Release?.TranslatedLanguage` —
        // a `ReleaseInfo` field, NOT a `Chapter` column). Wave 3 work is verification + a
        // reflection-guard living-documentation test + an explicit multi-release ranking case.
        // The sibling guard in MangaDownloadDecisionComparerFixture mirrors this shape.

        [Test]
        public void Spec_does_not_reference_Chapter_TranslatedLanguage()
        {
            // Phase 16 STRUCT-06 living documentation: STRUCT-04 removes Chapter.TranslatedLanguage
            // from the entity. Any spec reference would fail to compile; this test exists as a
            // structural guard that the spec is release-grain (reads Release.TranslatedLanguage,
            // NOT Chapter.TranslatedLanguage).
            typeof(NzbDrone.Core.Manga.Chapter)
                .GetProperty("TranslatedLanguage").Should().BeNull();
        }

        [Test]
        public void Multi_release_candidate_with_preferred_language_is_accepted()
        {
            // STRUCT-06 acceptance: ranking specs operate on release-grain. Build a RemoteChapter
            // whose Release carries a preferred language; assert IsSatisfiedBy returns Accept.
            // Multi-release semantics: the spec evaluates ONE candidate at a time — Phase 16's
            // guarantee is that swapping in a sibling ChapterRelease (different language) flips
            // the verdict purely by the Release.TranslatedLanguage axis with no Chapter-grain change.
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en", "es" }, allowOthers: false));

            var rc = BuildRemoteChapter(releaseLanguage: "en", translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void Multi_release_candidate_with_unsupported_language_is_rejected()
        {
            // STRUCT-06 acceptance: language outside profile is rejected (strict mode).
            // Sibling case to the preferred-language test above — same canonical chapter,
            // different ChapterRelease.TranslatedLanguage, opposite verdict. Confirms the
            // spec's language axis is release-grain.
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildTranslationProfile(7, new[] { "en", "es" }, allowOthers: false));

            var rc = BuildRemoteChapter(releaseLanguage: "ja", translationProfileId: 7);
            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.LanguageNotInProfile);
        }
    }
}
