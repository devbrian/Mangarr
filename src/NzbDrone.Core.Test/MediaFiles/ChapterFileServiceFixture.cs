using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling fixture per Phase 9 D-09-03 #3 + 09-01 audit
    // (covers gap-01 GetFilesByMangaIds, gap-02 FilterExistingFiles instance + static,
    // gap-05 GetFilesWithRelativePath). Role-match analog: MediaFileServiceTests/FilterFixture.cs
    // and the per-accessor coverage that exists across MediaFileService TV-side fixtures.
    [TestFixture]
    public class ChapterFileServiceFixture : CoreTest<ChapterFileService>
    {
        private Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = new Manga
                     {
                         Id = 10,
                         Path = @"C:\".AsOsAgnostic()
                     };
        }

        [Test]
        public void GetFilesByMangaIds_should_return_files_for_supplied_manga_ids()
        {
            var mangaIds = new List<int> { 1, 2, 3 };
            var expected = new List<ChapterFile>
            {
                new ChapterFile { Id = 1, MangaId = 1, RelativePath = "1.cbz" },
                new ChapterFile { Id = 2, MangaId = 2, RelativePath = "2.cbz" },
                new ChapterFile { Id = 3, MangaId = 3, RelativePath = "3.cbz" }
            };

            Mocker.GetMock<IChapterFileRepository>()
                .Setup(c => c.GetFilesByMangaIds(mangaIds))
                .Returns(expected);

            var result = Subject.GetFilesByMangaIds(mangaIds);

            result.Should().BeEquivalentTo(expected);
            Mocker.GetMock<IChapterFileRepository>()
                .Verify(c => c.GetFilesByMangaIds(mangaIds), Times.Once());
        }

        [Test]
        public void GetFilesByMangaIds_should_return_empty_list_when_no_matches()
        {
            var mangaIds = new List<int> { 99 };

            Mocker.GetMock<IChapterFileRepository>()
                .Setup(c => c.GetFilesByMangaIds(mangaIds))
                .Returns(new List<ChapterFile>());

            var result = Subject.GetFilesByMangaIds(mangaIds);

            result.Should().BeEmpty();
        }

        [Test]
        public void GetFilesWithRelativePath_should_delegate_to_repository()
        {
            var expected = new List<ChapterFile>
            {
                new ChapterFile { Id = 7, MangaId = 10, RelativePath = "ch-001.cbz" }
            };

            Mocker.GetMock<IChapterFileRepository>()
                .Setup(c => c.GetFilesWithRelativePath(10, "ch-001.cbz"))
                .Returns(expected);

            var result = Subject.GetFilesWithRelativePath(10, "ch-001.cbz");

            result.Should().BeEquivalentTo(expected);
            Mocker.GetMock<IChapterFileRepository>()
                .Verify(c => c.GetFilesWithRelativePath(10, "ch-001.cbz"), Times.Once());
        }

        [Test]
        public void FilterExistingFiles_should_return_all_files_when_no_chapterfile_rows_exist()
        {
            var files = new List<string>
            {
                "C:\\file1.cbz".AsOsAgnostic(),
                "C:\\file2.cbz".AsOsAgnostic(),
                "C:\\file3.cbz".AsOsAgnostic()
            };

            Mocker.GetMock<IChapterFileRepository>()
                .Setup(c => c.GetFilesByManga(It.IsAny<int>()))
                .Returns(new List<ChapterFile>());

            Subject.FilterExistingFiles(files, _manga).Should().BeEquivalentTo(files);
        }

        [Test]
        public void FilterExistingFiles_should_remove_files_that_already_exist_in_DB()
        {
            var files = new List<string>
            {
                "C:\\file1.cbz".AsOsAgnostic(),
                "C:\\file2.cbz".AsOsAgnostic(),
                "C:\\file3.cbz".AsOsAgnostic()
            };

            Mocker.GetMock<IChapterFileRepository>()
                .Setup(c => c.GetFilesByManga(It.IsAny<int>()))
                .Returns(new List<ChapterFile>
                {
                    new ChapterFile { RelativePath = "file2.cbz".AsOsAgnostic() }
                });

            var result = Subject.FilterExistingFiles(files, _manga);

            result.Should().HaveCount(2);
            result.Should().NotContain("C:\\file2.cbz".AsOsAgnostic());
        }

        [Test]
        public void FilterExistingFiles_static_should_use_supplied_chapterfile_list_without_DB_call()
        {
            var files = new List<string>
            {
                "C:\\file1.cbz".AsOsAgnostic(),
                "C:\\file2.cbz".AsOsAgnostic(),
                "C:\\file3.cbz".AsOsAgnostic()
            };

            var mangaFiles = new List<ChapterFile>
            {
                new ChapterFile { RelativePath = "file1.cbz".AsOsAgnostic() },
                new ChapterFile { RelativePath = "file3.cbz".AsOsAgnostic() }
            };

            var result = ChapterFileService.FilterExistingFiles(files, mangaFiles, _manga);

            result.Should().HaveCount(1);
            result.Should().Contain("C:\\file2.cbz".AsOsAgnostic());

            // Verify the static path did NOT touch the repository (caller's pre-fetched list is authoritative).
            Mocker.GetMock<IChapterFileRepository>()
                .Verify(c => c.GetFilesByManga(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void FilterExistingFiles_static_should_return_all_files_when_supplied_list_is_empty()
        {
            var files = new List<string>
            {
                "C:\\file1.cbz".AsOsAgnostic(),
                "C:\\file2.cbz".AsOsAgnostic()
            };

            var result = ChapterFileService.FilterExistingFiles(files, new List<ChapterFile>(), _manga);

            result.Should().BeEquivalentTo(files);
        }
    }
}
