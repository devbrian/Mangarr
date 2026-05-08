using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using MangaModel = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling fixture per Phase 9 D-09-05.
    // Role-match analog: src/NzbDrone.Core.Test/MediaFiles/UpgradeMediaFileServiceFixture.cs.
    //
    // PITFALL 4 ORDERING LOCK (RESEARCH §Threat 535): the
    // `should_call_recycleBin_DeleteFile_before_chapterFileService_Delete` test below uses
    // Moq's MockSequence to assert the recycle-FIRST → delete-row-SECOND sequence — the
    // inverse leaks the file path on disk if the recycle-bin step throws. NEVER invert.
    [TestFixture]
    public class UpgradeChapterFileServiceFixture : CoreTest<UpgradeChapterFileService>
    {
        private ChapterFile _chapterFile;
        private LocalChapter _localChapter;
        private ChapterFile _existingChapterFile;

        [SetUp]
        public void Setup()
        {
            var manga = new MangaModel { Path = @"C:\Test\Manga\Title".AsOsAgnostic() };

            _existingChapterFile = Builder<ChapterFile>.CreateNew()
                .With(cf => cf.Id = 17)
                .With(cf => cf.RelativePath = "ch11.cbz")
                .Build();

            var chapter = Builder<Chapter>.CreateNew()
                .With(c => c.Id = 42)
                .With(c => c.MangaId = 1)
                .With(c => c.ChapterFileId = (int?)17)
                .Build();

            _localChapter = new LocalChapter
            {
                Manga = manga,
                Chapter = chapter,
                Path = @"C:\Staging\Title\ch12.cbz".AsOsAgnostic()
            };

            _chapterFile = Builder<ChapterFile>.CreateNew()
                .With(cf => cf.Id = 99)
                .With(cf => cf.RelativePath = "ch12.cbz")
                .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localChapter.Manga.Path).FullName))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                  .Returns<string>(c => Path.GetDirectoryName(c));

            Mocker.GetMock<IChapterFileService>()
                  .Setup(c => c.Get(_existingChapterFile.Id))
                  .Returns(_existingChapterFile);
        }

        private void GivenNoExistingChapterFile()
        {
            _localChapter.Chapter.ChapterFileId = null;
        }

        [Test]
        public void should_call_recycleBin_DeleteFile_before_chapterFileService_Delete()
        {
            // PITFALL 4 ORDERING LOCK — RESEARCH §Threat 535 / PATTERNS §A.
            // Recycle (filesystem) MUST run BEFORE delete-row (DB) — inverse leaks the
            // file on disk if recycle throws.
            var sequence = new MockSequence();

            Mocker.GetMock<IRecycleBinProvider>().InSequence(sequence)
                .Setup(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("recycled/path");

            Mocker.GetMock<IChapterFileService>().InSequence(sequence)
                .Setup(c => c.Delete(It.IsAny<ChapterFile>(), It.IsAny<DeleteMediaFileReason>()));

            Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(_existingChapterFile, DeleteMediaFileReason.Upgrade), Times.Once);
        }

        [Test]
        public void should_pass_DeleteMediaFileReason_Upgrade_to_chapterFileService_Delete()
        {
            Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(_existingChapterFile, DeleteMediaFileReason.Upgrade), Times.Once);
        }

        [Test]
        public void should_log_warning_when_existing_file_missing_from_disk()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            // Recycle skipped, but DB row still removed (mirrors TV
            // should_delete_existing_file_fromdb_if_file_doesnt_exist).
            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(_existingChapterFile, DeleteMediaFileReason.Upgrade), Times.Once);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_skip_recycle_when_existing_ChapterFileId_is_null()
        {
            GivenNoExistingChapterFile();

            Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(It.IsAny<ChapterFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never);
        }

        [Test]
        public void should_skip_recycle_when_existing_ChapterFileId_is_zero_or_negative()
        {
            _localChapter.Chapter.ChapterFileId = 0;

            Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(It.IsAny<ChapterFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never);
        }

        [Test]
        public void should_populate_OldFiles_with_DeletedChapterFile_carrying_recycleBinPath()
        {
            const string recyclePath = "/recycle/bin/ch11.cbz";

            Mocker.GetMock<IRecycleBinProvider>()
                .Setup(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(recyclePath);

            var result = Subject.UpgradeChapterFile(_chapterFile, _localChapter);

            result.OldFiles.Should().HaveCount(1);
            result.OldFiles[0].ChapterFile.Should().BeSameAs(_existingChapterFile);
            result.OldFiles[0].RecycleBinPath.Should().Be(recyclePath);
        }

        [Test]
        public void should_throw_when_existing_file_present_and_root_folder_missing()
        {
            // Mirrors TV should_throw_if_there_are_existing_episode_files_and_the_root_folder_is_missing —
            // prevents leaving the old file on disk when we cannot recycle.
            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(Directory.GetParent(_localChapter.Manga.Path).FullName))
                .Returns(false);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeChapterFile(_chapterFile, _localChapter));

            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(It.IsAny<ChapterFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never);
        }

        [Test]
        public void should_call_MoveChapterFile_when_copyOnly_false()
        {
            Subject.UpgradeChapterFile(_chapterFile, _localChapter, copyOnly: false);

            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.MoveChapterFile(_chapterFile, _localChapter), Times.Once);
            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.CopyChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>()), Times.Never);
        }

        [Test]
        public void should_call_CopyChapterFile_when_copyOnly_true()
        {
            Subject.UpgradeChapterFile(_chapterFile, _localChapter, copyOnly: true);

            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.CopyChapterFile(_chapterFile, _localChapter), Times.Once);
            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.MoveChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>()), Times.Never);
        }

        [Test]
        public void should_skip_move_when_chapterFile_is_null_but_still_recycle_existing()
        {
            // D-09-05 — Phase 6 ImportApprovedChapters step 0.5 invokes UpgradeChapterFile
            // with a null new-file argument because the caller already owns the move via
            // _diskProvider.MoveFile. The recycle+delete-row side effect must still run; the
            // mover must NOT be called.
            Subject.UpgradeChapterFile(null, _localChapter);

            Mocker.GetMock<IRecycleBinProvider>()
                .Verify(r => r.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            Mocker.GetMock<IChapterFileService>()
                .Verify(c => c.Delete(_existingChapterFile, DeleteMediaFileReason.Upgrade), Times.Once);
            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.MoveChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>()), Times.Never);
            Mocker.GetMock<IMoveChapterFiles>()
                .Verify(m => m.CopyChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>()), Times.Never);
        }
    }
}
