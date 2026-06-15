using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // Guards the disk-aware reject spec added for debug session `rss-regrab-existing-chapter`
    // (2026-06-15): an already-imported chapter (ChapterFile present) must NOT be re-grabbed on
    // every RSS sync cycle. The spec is the decision-side peer of the import-side
    // MediaFiles/MangaImport/Specifications/UpgradeSpecification.cs gate.
    [TestFixture]
    public class UpgradeDiskSpecificationFixture
        : MangaDecisionEngineSpecFixtureBase<UpgradeDiskSpecification>
    {
        private const string MangaPath = "/library/Test Manga";

        [SetUp]
        public void Setup()
        {
            // Default: existing ChapterFiles are present on disk. The missing-on-disk path
            // (deferred to DeletedChapterFileSpecification) is exercised explicitly in its own test.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(true);
        }

        private TranslationProfile BuildUpgradeProfile(int id, IEnumerable<string> languages, bool upgradeAllowed)
        {
            var profile = BuildTranslationProfile(id, languages);
            profile.UpgradeAllowed = upgradeAllowed;
            return profile;
        }

        private ChapterFile BuildExistingFile(int id, string language)
        {
            return new ChapterFile
            {
                Id = id,
                TranslatedLanguage = language,
                ScanlationGroup = "Mangarr Gateway",
                Path = $"{MangaPath}/Chapter 0{id}.cbz",
                RelativePath = $"Chapter 0{id}.cbz"
            };
        }

        [Test]
        public void accepts_when_chapter_has_no_current_file()
        {
            // Gap-1 invariant: ChapterFileId == 0 (file deleted to force a redownload) → skip,
            // release stays grabbable. GetFilesByChapter must never even be consulted.
            var rc = BuildRemoteChapter(releaseLanguage: "en", chapterId: 100);
            rc.Chapters[0].ChapterFileId = 0;

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeTrue();
            Mocker.GetMock<IChapterFileService>()
                  .Verify(s => s.GetFilesByChapter(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void rejects_when_file_exists_and_upgrades_not_allowed()
        {
            // This is the production bug scenario: an imported chapter with the default upgrade
            // gate OFF was re-grabbed every RSS cycle. The spec must reject it.
            var rc = BuildRemoteChapter(releaseLanguage: "en", chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;
            rc.Manga.UpgradeAllowedOverride = false;

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { BuildExistingFile(7528, "en") });

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.DiskUpgradesNotAllowed);
        }

        [Test]
        public void rejects_when_file_exists_same_language_and_not_a_cf_upgrade()
        {
            // Upgrades allowed, but the candidate is the same language and no better CF score than
            // the existing file → not an upgrade → reject (the steady-state re-grab the bug caused).
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildUpgradeProfile(7, new[] { "en" }, upgradeAllowed: true));

            var rc = BuildRemoteChapter(releaseLanguage: "en", translationProfileId: 7, customFormatScore: 0, chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;
            rc.Manga.UpgradeAllowedOverride = true;

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { BuildExistingFile(7528, "en") });

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.DiskNotUpgrade);
        }

        [Test]
        public void rejects_when_existing_file_is_a_better_language_match()
        {
            // Existing "en" (rank 0) beats incoming "es" (rank 1) → candidate is a downgrade → reject.
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildUpgradeProfile(7, new[] { "en", "es" }, upgradeAllowed: true));

            var rc = BuildRemoteChapter(releaseLanguage: "es", translationProfileId: 7, chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;
            rc.Manga.UpgradeAllowedOverride = true;

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { BuildExistingFile(7528, "en") });

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.DiskNotUpgrade);
        }

        [Test]
        public void accepts_when_candidate_is_a_better_language_match()
        {
            // Incoming "en" (rank 0) beats existing "es" (rank 1) → genuine upgrade → accept.
            Mocker.GetMock<ITranslationProfileService>()
                  .Setup(s => s.Get(7))
                  .Returns(BuildUpgradeProfile(7, new[] { "en", "es" }, upgradeAllowed: true));

            var rc = BuildRemoteChapter(releaseLanguage: "en", translationProfileId: 7, chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;
            rc.Manga.UpgradeAllowedOverride = true;

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { BuildExistingFile(7528, "es") });

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_when_no_existing_files_resolved()
        {
            // ChapterFileId set but the FK resolves to no rows (orphaned) — nothing to compare;
            // let the release through rather than block on stale state.
            var rc = BuildRemoteChapter(releaseLanguage: "en", chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile>());

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void accepts_and_defers_when_existing_file_is_missing_on_disk()
        {
            // P2 (PR #372 review): the ChapterFile row exists but its CBZ is gone from disk.
            // This spec must NOT emit a permanent DiskUpgradesNotAllowed/DiskNotUpgrade here —
            // that would poison DeletedChapterFileSpecification's TEMPORARY ChapterNotMonitored
            // rejection (both run in the Disk priority bucket; a decision is TemporarilyRejected
            // only if ALL rejections are Temporary). A missing on-disk file is also a legitimate
            // re-download trigger. So: skip the missing file → Accept.
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(It.IsAny<string>()))
                  .Returns(false);

            var rc = BuildRemoteChapter(releaseLanguage: "en", chapterId: 100);
            rc.Manga.Path = MangaPath;
            rc.Chapters[0].ChapterFileId = 7528;
            rc.Manga.UpgradeAllowedOverride = false; // would reject if the missing file were counted

            Mocker.GetMock<IChapterFileService>()
                  .Setup(s => s.GetFilesByChapter(100))
                  .Returns(new List<ChapterFile> { BuildExistingFile(7528, "en") });

            var result = Subject.IsSatisfiedBy(rc, new ReleaseDecisionInformation());

            result.Accepted.Should().BeTrue();
        }
    }
}
