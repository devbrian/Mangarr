using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling fixture per Phase 9 D-09-03 #1.
    // Role-match analog: src/NzbDrone.Core.Test/MediaFiles/MediaFileTableCleanupServiceFixture.cs.
    [TestFixture]
    public class MangaFileTableCleanupServiceFixture : CoreTest<MangaFileTableCleanupService>
    {
        private const string DELETED_PATH = "ANY FILE WITH THIS PATH IS CONSIDERED DELETED!";
        private List<Chapter> _chapters;
        private Manga _manga;

        [SetUp]
        public void SetUp()
        {
            _chapters = Builder<Chapter>.CreateListOfSize(10)
                .Build()
                .ToList();

            _manga = Builder<Manga>.CreateNew()
                                   .With(m => m.Path = @"C:\Test\Manga\MangaTitle".AsOsAgnostic())
                                   .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(e => e.FileExists(It.Is<string>(c => !c.Contains(DELETED_PATH))))
                  .Returns(true);

            Mocker.GetMock<IChapterService>()
                  .Setup(c => c.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(_chapters);
        }

        private void GivenChapterFiles(IEnumerable<ChapterFile> chapterFiles)
        {
            Mocker.GetMock<IChapterFileService>()
                  .Setup(c => c.GetFilesByManga(It.IsAny<int>()))
                  .Returns(chapterFiles.ToList());
        }

        private void GivenFilesAreNotAttachedToChapter()
        {
            _chapters.ForEach(c => c.ChapterFileId = null);

            Mocker.GetMock<IChapterService>()
                  .Setup(c => c.GetChaptersByManga(It.IsAny<int>()))
                  .Returns(_chapters);
        }

        private List<string> FilesOnDisk(IEnumerable<ChapterFile> chapterFiles)
        {
            return chapterFiles.Select(f => Path.Combine(_manga.Path, f.RelativePath)).ToList();
        }

        [Test]
        public void should_skip_files_that_exist_in_disk()
        {
            var chapterFiles = Builder<ChapterFile>.CreateListOfSize(10)
                .Build();

            GivenChapterFiles(chapterFiles);

            Subject.Clean(_manga, FilesOnDisk(chapterFiles));

            Mocker.GetMock<IChapterService>().Verify(c => c.UpdateChapter(It.IsAny<Chapter>()), Times.Never());
        }

        [Test]
        public void should_delete_non_existent_files()
        {
            var chapterFiles = Builder<ChapterFile>.CreateListOfSize(10)
                .Random(2)
                .With(c => c.RelativePath = DELETED_PATH)
                .Build();

            GivenChapterFiles(chapterFiles);

            Subject.Clean(_manga, FilesOnDisk(chapterFiles.Where(f => f.RelativePath != DELETED_PATH)));

            Mocker.GetMock<IChapterFileService>().Verify(c => c.Delete(It.Is<ChapterFile>(f => f.RelativePath == DELETED_PATH), DeleteMediaFileReason.MissingFromDisk), Times.Exactly(2));
        }

        [Test]
        public void should_delete_files_that_dont_belong_to_any_chapters()
        {
            var chapterFiles = Builder<ChapterFile>.CreateListOfSize(10)
                                .Random(10)
                                .With(c => c.RelativePath = "ExistingPath")
                                .Build();

            GivenChapterFiles(chapterFiles);
            GivenFilesAreNotAttachedToChapter();

            Subject.Clean(_manga, FilesOnDisk(chapterFiles));

            Mocker.GetMock<IChapterFileService>().Verify(c => c.Delete(It.IsAny<ChapterFile>(), DeleteMediaFileReason.NoLinkedEpisodes), Times.Exactly(10));
        }

        [Test]
        public void should_unlink_chapter_when_chapterFile_does_not_exist()
        {
            // Make all chapters claim a ChapterFileId that no row in mangaFiles matches.
            for (var i = 0; i < _chapters.Count; i++)
            {
                _chapters[i].ChapterFileId = 1000 + i;
            }

            GivenChapterFiles(new List<ChapterFile>());

            Subject.Clean(_manga, new List<string>());

            Mocker.GetMock<IChapterService>().Verify(c => c.UpdateChapter(It.Is<Chapter>(ch => ch.ChapterFileId == null)), Times.Exactly(10));
        }

        [Test]
        public void should_not_update_chapter_when_chapterFile_exists()
        {
            var chapterFiles = Builder<ChapterFile>.CreateListOfSize(10)
                                .Random(10)
                                .With(c => c.RelativePath = "ExistingPath")
                                .Build();

            GivenChapterFiles(chapterFiles);

            Subject.Clean(_manga, FilesOnDisk(chapterFiles));

            Mocker.GetMock<IChapterService>().Verify(c => c.UpdateChapter(It.IsAny<Chapter>()), Times.Never());
        }
    }
}
