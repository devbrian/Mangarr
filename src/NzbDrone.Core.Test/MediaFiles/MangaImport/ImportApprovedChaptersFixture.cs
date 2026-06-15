using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;
using NzbDrone.Core.Messaging.Commands;
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
    // (Phase 39 RETIRE-01: the former "ChapterDownloadState row deleted on success" assertion was
    //  removed with the in-process download vertical — the gateway path creates no such row.)
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

        [Test]
        public void should_reject_and_queue_rescan_when_destination_already_exists()
        {
            // debug: import-retry-loop-file-exists — the chapter has NO ChapterFile row but the
            // computed library destination already holds a file (orphan-on-disk). DiskProviderBase.MoveFile
            // throws FileAlreadyExistsException — a DIFFERENT, unrelated type from the
            // DestinationAlreadyExistsException the canonical handler caught, so pre-fix it fell through to
            // the generic catch (ERROR + stack trace) and MangaCompletedDownloadService re-drove the row
            // every completed-download cycle (infinite loop). Sonarr-canonical outcome (mirrors
            // ImportApprovedEpisodes): reject the import ("destination already exists") + log Warn + queue a
            // RescanMangaCommand so the disk-scan reconciles the orphan into the DB. The importer must NOT
            // silently adopt the on-disk file — import moves NEW files, scan adopts EXISTING ones.
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.MoveFile(_stagingPath, _destPath, false))
                .Throws(new FileAlreadyExistsException("File already exists", _destPath));

            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            result.Should().HaveCount(1);

            // Approved decision carrying an error message → Skipped (MangaImportResult.Result:
            // Approved && Errors.Any() ⇒ Skipped); never a successful (Imported) result.
            result[0].Result.Should().Be(MangaImportResultType.Skipped);

            // No ChapterFile row is written (the move threw before the DB step) and neither the success
            // nor the failure event fires — the canonical handler only rejects + queues a rescan.
            Mocker.GetMock<IChapterFileService>().Verify(c => c.Add(It.IsAny<ChapterFile>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportFailedEvent>()), Times.Never);

            // Sonarr-canonical: a RescanMangaCommand for this manga is queued so the disk-scan
            // (MangaDiskScanService : IExecute<RescanMangaCommand>) reconciles the orphan into the DB.
            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.Is<RescanMangaCommand>(r => r.MangaId == _manga.Id),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_pass_overwrite_true_to_MoveFile_when_replace_requested()
        {
            // ExistingFileBehavior.Replace must reach the disk as overwrite:true so DiskProviderBase
            // deletes any existing destination BEFORE the move (a collision cannot occur in that case —
            // which is why the reject path above is gated on the no-overwrite scenario). Verifies the
            // per-row overwrite plumbing distinct from the destination-already-exists reject path.
            var decision = ApprovedDecision();
            decision.LocalChapter.ExistingFileBehavior = ExistingFileBehavior.Replace;

            var result = Subject.Import(new List<MangaImportDecision> { decision }, true, _downloadClientItem);

            result.Should().HaveCount(1);
            result[0].Result.Should().Be(MangaImportResultType.Imported);
            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.MoveFile(_stagingPath, _destPath, true), Times.Once);
        }

        // ---------------------------------------------------------------------
        // Phase 9 D-09-05 — UpgradeChapterFileService insertion-site coverage.
        // ---------------------------------------------------------------------

        [Test]
        public void should_call_UpgradeChapterFile_when_existing_ChapterFileId_present()
        {
            _chapter.ChapterFileId = 17;

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            // Recycle-only mode: first arg is null (caller owns the move via _diskProvider.MoveFile);
            // service performs only the recycle + delete-row side effect.
            Mocker.GetMock<IUpgradeChapterFiles>()
                .Verify(u => u.UpgradeChapterFile(null, It.IsAny<LocalChapter>(), false), Times.Once);
        }

        [Test]
        public void should_skip_UpgradeChapterFile_when_ChapterFileId_is_null()
        {
            _chapter.ChapterFileId = null;

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IUpgradeChapterFiles>()
                .Verify(u => u.UpgradeChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void should_skip_UpgradeChapterFile_when_ChapterFileId_is_zero()
        {
            _chapter.ChapterFileId = 0;

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IUpgradeChapterFiles>()
                .Verify(u => u.UpgradeChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>(), It.IsAny<bool>()), Times.Never);
        }

        [Test]
        public void should_call_UpgradeChapterFile_BEFORE_destination_path_build()
        {
            // Pitfall 4 ordering verification: recycle + delete-row of OLD file MUST happen before
            // destination-path build (and therefore before MoveFile + Add + PublishEvent of NEW file).
            _chapter.ChapterFileId = 17;

            var sequence = new MockSequence();

            Mocker.GetMock<IUpgradeChapterFiles>().InSequence(sequence)
                .Setup(u => u.UpgradeChapterFile(null, It.IsAny<LocalChapter>(), false));

            Mocker.GetMock<IBuildMangaPaths>().InSequence(sequence)
                .Setup(p => p.BuildChapterPath(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>(), It.IsAny<string>()))
                .Returns(_destPath);

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

            Mocker.GetMock<IUpgradeChapterFiles>()
                .Verify(u => u.UpgradeChapterFile(null, It.IsAny<LocalChapter>(), false), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_continue_import_when_upgrade_throws()
        {
            // Recycle failure is non-fatal in TV — same shape here: log Error + continue;
            // the new ChapterFile + ChapterImportedEvent still land. Old file leaks to disk
            // (rescan can reconcile). Surfacing as failure would block auto-retry orchestrator.
            _chapter.ChapterFileId = 17;

            Mocker.GetMock<IUpgradeChapterFiles>()
                .Setup(u => u.UpgradeChapterFile(It.IsAny<ChapterFile>(), It.IsAny<LocalChapter>(), It.IsAny<bool>()))
                .Throws(new RecycleBinException("simulated recycle failure"));

            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            // Import succeeded despite the recycle throw.
            result.Should().HaveCount(1);
            result[0].Result.Should().Be(MangaImportResultType.Imported);

            // ChapterImportedEvent fired (success); ChapterImportFailedEvent did NOT fire.
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportFailedEvent>()), Times.Never);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_use_lc_Size_for_ChapterFile_Size_when_authoritative()
        {
            // Phase 6 Plan 14 — BL-04. lc.Size is the authoritative size from Phase 4
            // staging; ChapterFile.Size must be sourced from it (not from a post-move
            // FileInfo lookup that can return 0 on transient I/O failure and silently
            // corrupt downstream ChapterHistory + ChapterImportMessage).
            const long authoritativeSize = 4096L * 1024L;   // 4 MiB

            var lc = new LocalChapter
            {
                Path = _stagingPath,
                Size = authoritativeSize,
                Manga = _manga,
                Chapter = _chapter,
                Chapters = new List<Chapter> { _chapter },
                Release = new ReleaseInfo { Title = "TestManga c012" },
                ExistingFile = false
            };

            ChapterFile captured = null;
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.Add(It.IsAny<ChapterFile>()))
                .Returns<ChapterFile>(cf =>
                {
                    cf.Id = 100;
                    captured = cf;
                    return cf;
                });

            Subject.Import(new List<MangaImportDecision> { new MangaImportDecision(lc) }, true, _downloadClientItem);

            captured.Should().NotBeNull("ChapterFileService.Add must have been called");
            captured.Size.Should().Be(authoritativeSize,
                "BL-04: ChapterFile.Size must come from lc.Size when > 0, not from post-move SafeGetFileSize.");
        }

        // ---------------------------------------------------------------------
        // Phase 38 Plan 38-02 (CINFO-01) — ComicInfoCbzInjector step-3.5 wire-site coverage.
        // ---------------------------------------------------------------------

        [Test]
        public void should_invoke_comicinfo_injector_once_per_approved_decision()
        {
            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            // D-A — invoked once per approved decision with the persisted ChapterFile + lc aggregates.
            Mocker.GetMock<IComicInfoCbzInjector>()
                .Verify(i => i.Inject(It.IsAny<ChapterFile>(), _manga, _chapter), Times.Once);
        }

        [Test]
        public void should_skip_comicinfo_injection_for_non_zip_archive_but_still_import()
        {
            // GH #311 review (P2): .cbr (RAR) / .cb7 (7z) are valid manga archives in the shared
            // import path (MangaFileExtensions — disk-scan + manual import). ComicInfoCbzInjector
            // opens ZipArchiveMode.Update and would throw on a non-ZIP file AFTER the ChapterFile
            // row is committed, failing the import of a valid archive. Injection is gated to
            // .cbz/.zip; non-ZIP formats import WITHOUT injection.
            var cbrDest = @"C:\Library\TestManga\ch12.cbr".AsOsAgnostic();
            Mocker.GetMock<IBuildMangaPaths>()
                .Setup(p => p.BuildChapterPath(It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>(), It.IsAny<string>()))
                .Returns(cbrDest);

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IComicInfoCbzInjector>()
                .Verify(i => i.Inject(It.IsAny<ChapterFile>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_invoke_comicinfo_injector_BEFORE_chapter_imported_event()
        {
            // Pitfall 4 / D-A2 ordering: the ComicInfo injection MUST complete before
            // ChapterImportedEvent is published (Komga/Kavita rescan handlers fire on that event).
            var sequence = new MockSequence();

            Mocker.GetMock<IChapterFileService>().InSequence(sequence)
                .Setup(c => c.Add(It.IsAny<ChapterFile>()))
                .Returns<ChapterFile>(cf =>
                {
                    cf.Id = 99;
                    return cf;
                });

            Mocker.GetMock<IComicInfoCbzInjector>().InSequence(sequence)
                .Setup(i => i.Inject(It.IsAny<ChapterFile>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>()));

            Mocker.GetMock<IEventAggregator>().InSequence(sequence)
                .Setup(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()));

            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            Mocker.GetMock<IComicInfoCbzInjector>()
                .Verify(i => i.Inject(It.IsAny<ChapterFile>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>()), Times.Once);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Once);
        }

        [Test]
        public void should_publish_failed_event_and_not_imported_event_when_injector_throws()
        {
            // D-B1 — a persisted injector failure re-throws and propagates to the existing
            // per-decision catch, which publishes ChapterImportFailedEvent ONCE. The chapter does
            // NOT land (ChapterImportedEvent NEVER fires). No duplicate failed-event publish.
            Mocker.GetMock<IComicInfoCbzInjector>()
                .Setup(i => i.Inject(It.IsAny<ChapterFile>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<Chapter>()))
                .Throws(new IOException("simulated persisted injection failure"));

            var result = Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, _downloadClientItem);

            result.Should().HaveCount(1);
            result[0].Errors.Should().NotBeEmpty();

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportedEvent>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterImportFailedEvent>()), Times.Once);

            // GH #311 review: the ChapterFile row committed at step 3 MUST be rolled back so a
            // failed injection doesn't leave an orphaned row (FK-wire/event steps were skipped).
            Mocker.GetMock<IChapterFileService>()
                .Verify(s => s.Delete(It.IsAny<ChapterFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Once);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_invoke_comicinfo_injector_unconditionally_with_no_download_client()
        {
            // D-A — the injector runs even for a manual-import-shaped decision (no download client
            // item passed). No gateway-only gate, no download-client-type discriminator.
            Subject.Import(new List<MangaImportDecision> { ApprovedDecision() }, true, downloadClientItem: null);

            Mocker.GetMock<IComicInfoCbzInjector>()
                .Verify(i => i.Inject(It.IsAny<ChapterFile>(), _manga, _chapter), Times.Once);
        }
    }
}
