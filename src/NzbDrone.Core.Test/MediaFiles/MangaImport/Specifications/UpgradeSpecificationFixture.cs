using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.MediaFiles.MangaImport.Specifications;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport.Specifications
{
    // Phase 6 Plan 06-07 — UpgradeSpecification D-10 three-state effective-upgrade-allowed:
    //   * manga.UpgradeAllowedOverride NULL  → fall back to per-profile flags AND-merged
    //   * manga.UpgradeAllowedOverride TRUE  → force-allow (per-profile flags ignored)
    //   * manga.UpgradeAllowedOverride FALSE → force-disallow (per-profile flags ignored)
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private Chapter _chapter;
        private LocalChapter _localChapter;
        private TranslationProfile _translationProfile;
        private CustomFormatProfile _cfProfile;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 5)
                .With(m => m.TranslationProfileId = (int?)10)
                .With(m => m.CustomFormatProfileId = (int?)20)
                .With(m => m.UpgradeAllowedOverride = (bool?)null)
                .Build();

            _chapter = Builder<Chapter>.CreateNew().With(c => c.Id = 100).Build();

            _localChapter = new LocalChapter
            {
                Manga = _manga,
                Chapter = _chapter,
                Chapters = new List<Chapter> { _chapter },
                TranslatedLanguage = "en",
                CustomFormatScore = 50
            };

            _translationProfile = new TranslationProfile
            {
                Id = 10,
                Languages = new List<string> { "en", "es", "ja" },
                UpgradeAllowed = true   // D-10 default TRUE
            };

            _cfProfile = new CustomFormatProfile
            {
                Id = 20,
                FormatItems = new List<ProfileFormatItem>(),
                UpgradeAllowed = false  // D-10 default FALSE
            };

            Mocker.GetMock<ITranslationProfileService>()
                .Setup(s => s.Get(10)).Returns(_translationProfile);
            Mocker.GetMock<ICustomFormatProfileService>()
                .Setup(s => s.Get(20)).Returns(_cfProfile);

            // Existing file present → upgrade path is the gate.
            var existing = new ChapterFile
            {
                Id = 1, MangaId = 5, ChapterId = 100,
                TranslatedLanguage = "en"
            };
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByChapter(100))
                .Returns(new List<ChapterFile> { existing });

            // CF calculator returns no matches by default.
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>()))
                .Returns(new List<CustomFormat>());
        }

        [Test]
        public void no_existing_file_should_accept()
        {
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByChapter(100))
                .Returns(new List<ChapterFile>());

            Subject.IsSatisfiedBy(_localChapter, null).Accepted.Should().BeTrue();
        }

        // D-10 three-state — STATE 1: per-Manga override NULL, profile flags AND-merge to FALSE
        [Test]
        public void d10_state1_override_null_and_profiles_merge_false_should_reject_not_upgrade_allowed()
        {
            _manga.UpgradeAllowedOverride = null;
            _translationProfile.UpgradeAllowed = true;
            _cfProfile.UpgradeAllowed = false;   // AND-merge → false

            var decision = Subject.IsSatisfiedBy(_localChapter, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.NotUpgradeAllowed);
        }

        // D-10 three-state — STATE 1b: per-Manga override NULL, profile flags AND-merge to TRUE → fall through to comparison
        [Test]
        public void d10_state1b_override_null_and_profiles_merge_true_should_accept_at_better_rank()
        {
            _manga.UpgradeAllowedOverride = null;
            _translationProfile.UpgradeAllowed = true;
            _cfProfile.UpgradeAllowed = true;

            // existing TranslatedLanguage="en" (rank 0); incoming "es" (rank 1) → existing wins → reject
            _localChapter.TranslatedLanguage = "es";

            var decision = Subject.IsSatisfiedBy(_localChapter, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.NotUpgrade);
        }

        // D-10 three-state — STATE 2: per-Manga override TRUE forces allow regardless of profile flags
        [Test]
        public void d10_state2_override_true_should_force_allow_even_when_profiles_say_no()
        {
            _manga.UpgradeAllowedOverride = true;
            _translationProfile.UpgradeAllowed = false;
            _cfProfile.UpgradeAllowed = false;

            // incoming "ja" (rank 2), existing "en" (rank 0) → existing wins → reject as NotUpgrade
            // but the override-true means we still pass the AllowedGate; rejection is downstream comparison.
            _localChapter.TranslatedLanguage = "ja";

            var decision = Subject.IsSatisfiedBy(_localChapter, null);
            decision.Accepted.Should().BeFalse();

            // Reject reason is NotUpgrade (existing better rank), NOT NotUpgradeAllowed (which would
            // mean the gate blocked the comparison entirely).
            decision.Reason.Should().Be(ImportRejectionReason.NotUpgrade);
        }

        // D-10 three-state — STATE 3: per-Manga override FALSE forces disallow regardless of profile flags
        [Test]
        public void d10_state3_override_false_should_force_disallow_even_when_profiles_say_yes()
        {
            _manga.UpgradeAllowedOverride = false;
            _translationProfile.UpgradeAllowed = true;
            _cfProfile.UpgradeAllowed = true;

            var decision = Subject.IsSatisfiedBy(_localChapter, null);
            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.NotUpgradeAllowed);
        }

        [Test]
        public void better_language_rank_should_accept_when_upgrade_allowed()
        {
            _manga.UpgradeAllowedOverride = true;   // force-allow gate

            // existing "en" (rank 0); incoming new manga that has different existing rank
            // To prove "better incoming wins", set existing to "ja" (rank 2) and incoming to "en" (rank 0).
            Mocker.GetMock<IChapterFileService>()
                .Setup(s => s.GetFilesByChapter(100))
                .Returns(new List<ChapterFile>
                {
                    new ChapterFile { Id = 1, ChapterId = 100, TranslatedLanguage = "ja" }
                });
            _localChapter.TranslatedLanguage = "en";

            Subject.IsSatisfiedBy(_localChapter, null).Accepted.Should().BeTrue();
        }
    }
}
