using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HistoryTests.Manga
{
    // Phase 6 D-21 — exercises each of the 5 IHandle handlers on ChapterHistoryService
    // and asserts the per-EventType Data key set is populated per the Q-4 schema lock.
    [TestFixture]
    public class ChapterHistoryServiceFixture : CoreTest<ChapterHistoryService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private Chapter _chapter;

        [SetUp]
        public void Setup()
        {
            _manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" };
            _chapter = new Chapter { Id = 42, MangaId = 7, ChapterNumber = 1m };
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_inserts_row_with_Grabbed_Data_keys()
        {
            var release = new ReleaseInfo
            {
                Title = "Test Manga - Chapter 001",
                Indexer = "MangaDex",
                Guid = "guid-grab-1",
                TranslatedLanguage = "en",
                ScanlationGroup = "Group",
                Size = 1024,
                DownloadProtocol = DownloadProtocol.Http
            };
            var remote = new RemoteChapter
            {
                Manga = _manga,
                Chapters = new List<Chapter> { _chapter },
                Release = release,
                CustomFormatScore = 100
            };

            Subject.Handle(new ChapterGrabbedEvent(remote, "dl-1", "InProcess"));

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.Insert(It.Is<ChapterHistory>(h =>
                h.EventType == ChapterHistoryEventType.Grabbed &&
                h.MangaId == 7 &&
                h.ChapterId == 42 &&
                h.SourceTitle == "Test Manga - Chapter 001" &&
                h.DownloadId == "dl-1" &&
                h.SourceKey == "MangaDex" &&
                h.ReleaseGuid == "guid-grab-1" &&
                h.Successful == true &&
                h.Data.ContainsKey("Indexer") &&
                h.Data.ContainsKey("Age") &&
                h.Data.ContainsKey("PublishedDate") &&
                h.Data.ContainsKey("DownloadClient") &&
                h.Data.ContainsKey("Size") &&
                h.Data.ContainsKey("Protocol") &&
                h.Data.ContainsKey("CustomFormatScore"))));
        }

        [Test]
        public void Handle_ChapterImportedEvent_inserts_row_with_Imported_Data_keys()
        {
            var chapterFile = new ChapterFile
            {
                Id = 99,
                MangaId = 7,
                ChapterId = 42,
                Path = "/manga/Test/Test - 001.cbz",
                RelativePath = "Test - 001.cbz",
                Size = 2048,
                TranslatedLanguage = "en"
            };
            var importedEvent = new ChapterImportedEvent
            {
                Manga = _manga,
                Chapter = _chapter,
                ChapterFile = chapterFile,
                SourcePath = "/dropped/Test - 001.cbz",
                NewDownload = true,
                DownloadClientItem = new DownloadClientItem
                {
                    DownloadId = "dl-imp-1",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Type = "InProcess" }
                }
            };

            Subject.Handle(importedEvent);

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.Insert(It.Is<ChapterHistory>(h =>
                h.EventType == ChapterHistoryEventType.Imported &&
                h.MangaId == 7 &&
                h.ChapterId == 42 &&
                h.DownloadId == "dl-imp-1" &&
                h.Successful == true &&
                h.Data.ContainsKey("ChapterFileId") &&
                h.Data.ContainsKey("DroppedPath") &&
                h.Data.ContainsKey("ImportedPath") &&
                h.Data.ContainsKey("Size") &&
                h.Data.ContainsKey("DownloadClient"))));
        }

        [Test]
        public void Handle_ChapterDownloadFailedEvent_inserts_row_with_DownloadFailed_Data_keys()
        {
            var release = new ReleaseInfo
            {
                Title = "Test Manga - Chapter 001",
                Indexer = "MangaDex",
                Guid = "guid-fail-1"
            };
            var failedEvent = new ChapterDownloadFailedEvent(rowId: 5, mangaId: 7, chapterId: 42, failureReason: "404 image missing")
            {
                SourceTitle = "Test Manga - Chapter 001",
                Source = "ImageDownload",
                DownloadClient = "InProcess",
                Release = release
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.Insert(It.Is<ChapterHistory>(h =>
                h.EventType == ChapterHistoryEventType.DownloadFailed &&
                h.MangaId == 7 &&
                h.ChapterId == 42 &&
                h.SourceTitle == "Test Manga - Chapter 001" &&
                h.SourceKey == "MangaDex" &&
                h.ReleaseGuid == "guid-fail-1" &&
                h.Successful == false &&
                h.Data.ContainsKey("DownloadClient") &&
                h.Data.ContainsKey("Message") &&
                h.Data.ContainsKey("Source") &&
                h.Data.ContainsKey("Indexer"))));
        }

        [Test]
        public void Handle_MangaDeletedEvent_cascades_delete_for_manga()
        {
            var deleted = new MangaDeletedEvent(_manga, deleteFiles: true);

            Subject.Handle(deleted);

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.DeleteForManga(7), Times.Once);
        }

        [Test]
        public void Handle_ChapterImportFailedEvent_inserts_row_with_ImportFailed_Data_keys()
        {
            var failedEvent = new ChapterImportFailedEvent
            {
                Manga = _manga,
                Chapter = _chapter,
                SourcePath = "/dropped/Test - 001.cbz",
                FailureReason = "Root folder is missing",
                DownloadClientItem = new DownloadClientItem
                {
                    DownloadId = "dl-impfail-1",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Type = "Gateway" }
                }
            };

            Subject.Handle(failedEvent);

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.Insert(It.Is<ChapterHistory>(h =>
                h.EventType == ChapterHistoryEventType.ImportFailed &&
                h.MangaId == 7 &&
                h.ChapterId == 42 &&
                h.DownloadId == "dl-impfail-1" &&
                h.Successful == false &&
                h.Data["FailureReason"] == "Root folder is missing" &&
                h.Data.ContainsKey("DroppedPath") &&
                h.Data.ContainsKey("RejectionType"))));
        }

        [Test]
        public void Handle_ChapterImportIgnoredEvent_inserts_row_with_Ignored_Data_keys()
        {
            var ignoredEvent = new ChapterImportIgnoredEvent
            {
                Manga = _manga,
                Chapter = _chapter,
                SourcePath = "/staging/Test - 001.cbz",
                Reason = "Chapter already imported — not re-importing this download",
                RejectionType = "ChapterAlreadyImported",
                Indexer = "MangaDex",
                TranslatedLanguage = "en",
                ScanlationGroup = "Speedcat",
                DownloadClientItem = new DownloadClientItem
                {
                    DownloadId = "dl-ignored-1",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Type = "Gateway" }
                }
            };

            Subject.Handle(ignoredEvent);

            Mocker.GetMock<IChapterHistoryRepository>().Verify(r => r.Insert(It.Is<ChapterHistory>(h =>
                h.EventType == ChapterHistoryEventType.Ignored &&
                h.MangaId == 7 &&
                h.ChapterId == 42 &&
                h.DownloadId == "dl-ignored-1" &&
                h.SourceKey == "MangaDex" &&
                h.TranslatedLanguage == "en" &&
                h.ScanlationGroup == "Speedcat" &&
                h.Successful == false &&
                h.Data["Message"] == "Chapter already imported — not re-importing this download" &&
                h.Data["RejectionType"] == "ChapterAlreadyImported" &&
                h.Data.ContainsKey("DownloadClient") &&
                h.Data.ContainsKey("Indexer"))));
        }

        [Test]
        public void FindByChapterId_pass_through_to_repository()
        {
            Mocker.GetMock<IChapterHistoryRepository>()
                .Setup(r => r.FindByChapterId(42))
                .Returns(new List<ChapterHistory> { new() { ChapterId = 42, EventType = ChapterHistoryEventType.Grabbed } });

            var result = Subject.FindByChapterId(42);

            result.Should().HaveCount(1);
            result[0].ChapterId.Should().Be(42);
        }
    }
}
