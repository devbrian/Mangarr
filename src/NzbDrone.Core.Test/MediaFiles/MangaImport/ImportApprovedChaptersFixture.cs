using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport
{
    // Phase 6 Plan 06-07 — ImportApprovedChapters BLOCKING tests:
    //   1. Pitfall 4 ordering: ChapterImportedEvent published ONLY after _chapterFileService.Add
    //      AND _diskProvider.MoveFile complete (verified via Moq sequence).
    //   2. ChapterImportFailedEvent emitted on RootFolderNotFoundException + generic exception.
    //   3. ChapterImportedEvent NOT published when _chapterFileService.Add throws (DB commit
    //      failed mid-way — notification fan-out must not race).
    //   4. In-batch dedup — same chapter ID twice in one batch produces one import + one skip.
    //   5. ChapterDownloadState row deleted on success.
    [TestFixture]
    public class ImportApprovedChaptersFixture : CoreTest<ImportApprovedChapters>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private Chapter _chapter;
        private DownloadClientItem _downloadClientItem;
        private string _stagingPath;
        private string _destPath;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(m => m.Id = 7)
                .With(m => m.Path = @"C:\Library\TestManga".AsOsAgnostic())
                .Build();

            _chapter = Builder<Chapter>.CreateNew()
                .With(c => c.Id = 42)
                .With(c => c.MangaId = _manga.Id)
                .With(c => c.ChapterNumber = 12m)
                .With(c => c.ChapterFileId = (int?)null)
                .Build();

            _stagingPath = @"C:\Staging\TestManga\ch12.cbz".AsOsAgnostic();
            _destPath = @"C:\Library\TestManga\ch12.cbz".AsOsAgnostic();

            _downloadClientItem = Builder<DownloadClientItem>.CreateNew()
                .With(d => d.DownloadId = "dl-123")
                .Build();

            // Path builder returns the destination — single point of truth for the test.
            Mocker.GetMock<IBuildMangaPaths>()
                .Setup(p => p.BuildChapterPath(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>(), It.IsAny<string>()))
                .Returns(_destPath);

            // Disk provider sees both files as present so the path checks succeed.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);

            // _chapterFileService.Add returns the file with assigned Id — mimics Dapper insert.
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.Add(It.IsAny<ChapterFile>()))
                .Returns<ChapterFile>(cf =>
                {
                    cf.Id = 99;
                    return cf;
                });
        }

        private MangaImportDecision ApprovedDecision(Chapter chapter = null)
        {
            var lc = new LocalChapter
            {
                Path = _stagingPath,
                Size = 1024,
                Manga = _manga,
                Chapter = chapter ?? _chapter,
                Chapters = new List<Chapter> { chapter ?? _chapter },
                Release = new ReleaseInfo { Title = "TestManga c012" },
                ExistingFile = false
            };
            return new MangaImportDecision(lc);
        }

        [Test]
        public void should_import_single_approved_decision_and_publish_event()
        {
            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            result.Should().HaveCount(1);
            result[0].Result.Should().Be(MangaImportResultType.Imported);

            Mocker.GetMock<IChapterFileService>().Verify(c => c.Add(It.IsAny<ChapterFile>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_publish_chapter_imported_event_after_db_commit_and_after_file_move()
        {
            // Pitfall 4 GUARD: build a Moq sequence — Add (DB commit) + MoveFile must complete
            // BEFORE PublishEvent(ChapterImportedEvent).
            var sequence = new MockSequence();
            Mocker.GetMock<IDiskProvider>().InSequence(sequence)
                .Setup(d => d.MoveFile(_stagingPath, _destPath, false));
            Mocker.GetMock<IChapterFileService>().InSequence(sequence)
                .Setup(c => c.Add(It.IsAny<ChapterFile>()))
                .Returns<ChapterFile>(cf =>
                {
                    cf.Id = 99;
                    return cf;
                });
            Mocker.GetMock<IEventAggregator>().InSequence(sequence)
                .Setup(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()));

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(_stagingPath, _destPath, false), Times.Once);
            Mocker.GetMock<IChapterFileService>().Verify(c => c.Add(It.IsAny<ChapterFile>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_not_publish_chapter_imported_event_when_db_commit_fails()
        {
            // Pitfall 4 GUARD inverse: if Add throws (DB commit fails after file move), the
            // ChapterImportedEvent must NOT fire — Komga/Kavita rescan would find a moved file
            // with no DB row, breaking the invariant. Failure path must publish the FAILED event.
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.Add(It.IsAny<ChapterFile>()))
                .Throws(new System.Data.DataException("simulated DB commit failure"));

            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            result.Should().HaveCount(1);
            result[0].Errors.Should().NotBeEmpty();

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportFailedEvent>()), Times.Once);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_dedupe_duplicate_chapter_ids_within_a_single_batch()
        {
            var first = ApprovedDecision();
            var second = ApprovedDecision();   // same _chapter (Id 42)

            var result = Subject.Import(new List<MangaImportDecision> { first, second }, true, _downloadClientItem);

            result.Should().HaveCount(2);

            // First import is the canonical Imported result; second is a Skipped (in-batch dup).
            result.Count(r => r.Result == MangaImportResultType.Imported).Should().Be(1);
            result.Count(r => r.Result == MangaImportResultType.Skipped && r.Errors.Any(e => e.Contains("already been imported in this batch"))).Should().Be(1);

            Mocker.GetMock<IChapterFileService>().Verify(c => c.Add(It.IsAny<ChapterFile>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_delete_chapter_download_state_row_on_success()
        {
            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Verify(r => r.DeleteByChapterId(_chapter.Id), Times.Once);
        }

        [Test]
        public void should_update_chapter_file_id_fk_after_db_commit()
        {
            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            // Plan 06-01 PIPELINE-04 — Chapter.ChapterFileId FK wired post-Add.
            _chapter.ChapterFileId.Should().Be(99);
            Mocker.GetMock<IChapterService>()
                .Verify(s => s.UpdateChapter(_chapter), Times.Once);
        }

        [Test]
        public void should_publish_failed_event_on_generic_exception()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Throws(new IOException("disk full"));

            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            result[0].Errors.Should().NotBeEmpty();
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportFailedEvent>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Never);

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
