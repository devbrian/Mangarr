using System;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Bug fix delete-files-not-removed (Issue #33, 2026-05-09): exercises the
    // ChapterFileService.HandleAsync(MangaDeletedEvent) path that now honors
    // MangaDeletedEvent.DeleteFiles by removing the manga's root folder via
    // IRecycleBinProvider.DeleteFolder. Prior behavior cascade-deleted only the
    // ChapterFile DB rows — the on-disk folder survived. Mirrors TV's
    // MediaFileService.HandleAsync(SeriesDeletedEvent) shape.
    [TestFixture]
    public class ChapterFileServiceHandleMangaDeletedEventFixture : CoreTest<ChapterFileService>
    {
        private Manga _manga;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<Manga>.CreateNew()
                .With(m => m.Id = 42)
                .With(m => m.Title = "Test Manga")
                .With(m => m.Path = @"C:\Test\Manga\Title".AsOsAgnostic())
                .Build();
        }

        [Test]
        public void HandleAsync_should_cascade_delete_db_rows_when_DeleteFiles_is_false()
        {
            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: false));

            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.DeleteForManga(_manga.Id), Times.Once());
        }

        [Test]
        public void HandleAsync_should_NOT_call_recycle_bin_when_DeleteFiles_is_false()
        {
            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(r => r.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void HandleAsync_should_recycle_manga_folder_when_DeleteFiles_is_true_and_folder_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_manga.Path))
                  .Returns(true);

            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: true));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(r => r.DeleteFolder(_manga.Path), Times.Once());
        }

        [Test]
        public void HandleAsync_should_still_cascade_db_rows_when_DeleteFiles_is_true()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_manga.Path))
                  .Returns(true);

            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: true));

            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.DeleteForManga(_manga.Id), Times.Once());
        }

        [Test]
        public void HandleAsync_should_skip_recycle_when_folder_does_not_exist()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_manga.Path))
                  .Returns(false);

            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: true));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(r => r.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.DeleteForManga(_manga.Id), Times.Once());
        }

        [Test]
        public void HandleAsync_should_skip_recycle_when_path_is_null_or_whitespace()
        {
            _manga.Path = null;

            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: true));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(r => r.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>()
                  .Verify(d => d.FolderExists(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.DeleteForManga(_manga.Id), Times.Once());
        }

        [Test]
        public void HandleAsync_should_still_cascade_db_rows_when_recycle_throws()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FolderExists(_manga.Path))
                  .Returns(true);
            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(r => r.DeleteFolder(_manga.Path))
                  .Throws(new InvalidOperationException("recycle blew up"));

            Subject.HandleAsync(new MangaDeletedEvent(_manga, deleteFiles: true));

            // Recycle threw, but the cascade DB delete must still run so the manga's
            // ChapterFile rows do not survive a delete that the controller already
            // committed. The exception is logged and swallowed at the handler boundary.
            Mocker.GetMock<IChapterFileRepository>()
                  .Verify(r => r.DeleteForManga(_manga.Id), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
