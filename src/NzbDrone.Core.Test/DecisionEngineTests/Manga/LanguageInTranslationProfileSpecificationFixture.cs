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

        // Phase 16.1 Wave 3 (REVERT-03): the spec already operated at the release-grain
        // (`subject.Release?.TranslatedLanguage` — a `ReleaseInfo` field, the indexer-time
        // projection produced by RemoteChapter.Release). The Phase 16 persistent
        // per-translation row layer was reverted, but this spec was a verify-only no-op
        // throughout (Pitfall #2 in the Phase 16.1 PATTERNS.md). Tests below are the
        // structural reflection-guard + a multi-candidate ranking case for the indexer
        // projection. Sibling guard in MangaDownloadDecisionComparerFixture mirrors this shape.

        [Test]
        public void Spec_does_not_reference_Chapter_TranslatedLanguage()
        {
            // Living-documentation guard: Chapter has no TranslatedLanguage property (the
            // language axis lives on the indexer projection RemoteChapter.Release, NOT on
            // the canonical Chapter entity). Any spec read from Chapter.TranslatedLanguage
            // would fail to compile; this test exists as a structural guard that the spec
            // remains release-grain.
            typeof(NzbDrone.Core.Manga.Chapter)
                .GetProperty("TranslatedLanguage").Should().BeNull();
        }

        [Test]
        public void Multi_release_candidate_with_preferred_language_is_accepted()
        {
            // Ranking specs operate at release-grain. Build a RemoteChapter whose Release
            // carries a preferred language; assert IsSatisfiedBy returns Accept. Swapping
            // the candidate's RemoteChapter.Release.TranslatedLanguage flips the verdict
            // purely by that axis — no Chapter-grain change required.
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
            // Sibling case to the preferred-language test above — same canonical chapter,
            // different RemoteChapter.Release.TranslatedLanguage, opposite verdict.
            // Confirms the spec's language axis is release-grain (indexer projection).
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
