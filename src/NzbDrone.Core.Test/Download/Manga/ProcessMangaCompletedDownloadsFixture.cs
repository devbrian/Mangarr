using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 6 Plan 06-08 — ProcessMangaCompletedDownloads BLOCKING tests:
    //
    //   PATTERN 1 (RESEARCH lines 430-457) — hybrid event-handler + poller. Both paths
    //   converge on the same ProcessOne method. Tests cover:
    //     1. Reactive path: ChapterArchivedEvent → ProcessOne → ImportApprovedChapters.Import
    //     2. Poll path: Execute(ProcessMangaCompletedCommand) → ByStatus(Completed) → ProcessOne
    //     3. Idempotency: ChapterFile already exists → skip (no Import call)
    //     4. Lifecycle: on success, ChapterDownloadState row is deleted (Phase 4 D-08)
    //     5. Concurrency: even if both paths fire for the same chapter, the idempotency check
    //        means Import is invoked at most once (the first ProcessOne caller wins; the
    //        second sees ChapterFile present and bails). Asserted in
    //        Both_paths_invoking_same_chapter_only_imports_once.
    //     6. Q-8 reconciliation: import-spec rejection does NOT delete the state row — Phase 4
    //        housekeeper owns retention sweep.
    [TestFixture]
    public class ProcessMangaCompletedDownloadsFixture : CoreTest<ProcessMangaCompletedDownloads>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private Chapter _chapter;
        private ChapterDownloadState _state;
        private string _stagingPath;

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

            _stagingPath = @"C:\Staging\7\ch12.cbz".AsOsAgnostic();

            _state = new ChapterDownloadState
            {
                Id = 99,
                MangaId = _manga.Id,
                ChapterId = _chapter.Id,
                Status = ChapterDownloadStatus.Completed,
                StagingPath = _stagingPath
            };

            // Default: services resolve everything successfully.
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(It.IsAny<int>()))
                .Returns(new List<ChapterFile>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(It.IsAny<string>()))
                .Returns(1024L);

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChapter(_chapter.Id))
                .Returns(_chapter);

            Mocker.GetMock<IMangaService>()
                .Setup(s => s.GetManga(_manga.Id))
                .Returns(_manga);

            // Default decision-maker returns approved decision.
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                .Returns<LocalChapter, NzbDrone.Core.Download.DownloadClientItem>((lc, _) => new MangaImportDecision(lc));

            // Default importer returns Imported result.
            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(),
                                     It.IsAny<bool>(),
                                     It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                .Returns<List<MangaImportDecision>, bool, NzbDrone.Core.Download.DownloadClientItem>((decisions, _, __) =>
                {
                    var results = new List<MangaImportResult>();
                    foreach (var d in decisions)
                    {
                        var cf = new ChapterFile
                        {
                            Id = 5,
                            MangaId = d.LocalChapter.Manga.Id,
                            ChapterId = d.LocalChapter.Chapter.Id,
                            Path = d.LocalChapter.Path
                        };
                        results.Add(new MangaImportResult(d, cf));
                    }

                    return results;
                });
        }

        // ── REACTIVE PATH (IHandle<ChapterArchivedEvent>) ───────────────────────────────

        [Test]
        public void Reactive_path_ChapterArchivedEvent_dispatches_into_ImportApprovedChapters()
        {
            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), true, null), Times.Once);
        }

        [Test]
        public void Reactive_path_skips_when_chapter_file_already_exists_idempotency()
        {
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(_chapter.Id))
                .Returns(new List<ChapterFile> { new ChapterFile { Id = 1, Path = _stagingPath } });

            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()),
                    Times.Never);
        }

        [Test]
        public void Reactive_path_skips_when_staging_file_missing()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(_stagingPath))
                .Returns(false);

            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()),
                    Times.Never);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Reactive_path_deletes_ChapterDownloadState_row_on_import_success()
        {
            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            // Phase 4 D-08 lifecycle hook — state row deleted on success.
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Verify(r => r.DeleteByChapterId(_chapter.Id), Times.Once);
        }

        [Test]
        public void Reactive_path_does_not_delete_state_row_when_decision_rejected_Q8()
        {
            // Q-8 reconciliation: leave row for Phase 4 housekeeper retention sweep.
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                .Returns<LocalChapter, NzbDrone.Core.Download.DownloadClientItem>((lc, _) =>
                    new MangaImportDecision(lc, new MangaImportRejection(ImportRejectionReason.NotUpgradeAllowed, "rejected by upgrade gate")));

            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()),
                    Times.Never);
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Verify(r => r.DeleteByChapterId(It.IsAny<int>()), Times.Never);
        }

        // ── POLL PATH (IExecute<ProcessMangaCompletedCommand>) ──────────────────────────

        [Test]
        public void Poll_path_reads_completed_state_rows_and_dispatches_each()
        {
            var second = new ChapterDownloadState
            {
                Id = 100,
                MangaId = _manga.Id,
                ChapterId = 43,
                Status = ChapterDownloadStatus.Completed,
                StagingPath = @"C:\Staging\7\ch13.cbz".AsOsAgnostic()
            };

            var chapter43 = Builder<Chapter>.CreateNew().With(c => c.Id = 43).With(c => c.MangaId = _manga.Id).Build();
            Mocker.GetMock<IChapterService>().Setup(s => s.GetChapter(43)).Returns(chapter43);

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.ByStatus(ChapterDownloadStatus.Completed))
                .Returns(new List<ChapterDownloadState> { _state, second });

            Subject.Execute(new ProcessMangaCompletedCommand());

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), true, null), Times.Exactly(2));
        }

        [Test]
        public void Poll_path_no_completed_rows_does_not_invoke_importer()
        {
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.ByStatus(ChapterDownloadStatus.Completed))
                .Returns(new List<ChapterDownloadState>());

            Subject.Execute(new ProcessMangaCompletedCommand());

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()),
                    Times.Never);
        }

        [Test]
        public void Poll_path_skips_row_when_chapter_file_already_imported_idempotency()
        {
            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.ByStatus(ChapterDownloadStatus.Completed))
                .Returns(new List<ChapterDownloadState> { _state });

            // Existing ChapterFile means a prior import already succeeded — poll path must skip.
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(_chapter.Id))
                .Returns(new List<ChapterFile> { new ChapterFile { Id = 1, Path = _stagingPath } });

            Subject.Execute(new ProcessMangaCompletedCommand());

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()),
                    Times.Never);
        }

        // ── CONCURRENCY (Pattern 1 idempotency contract) ────────────────────────────────

        [Test]
        public void Both_paths_invoking_same_chapter_only_imports_once_idempotency_contract()
        {
            // Simulate concurrency: reactive event fires and successfully imports (state below
            // tracks call count). The next caller — whether reactive or poll — sees the
            // ChapterFile present and bails.
            var importCount = 0;
            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<NzbDrone.Core.Download.DownloadClientItem>()))
                .Returns<List<MangaImportDecision>, bool, NzbDrone.Core.Download.DownloadClientItem>((decisions, _, __) =>
                {
                    importCount++;

                    // After the first import, ChapterFile exists.
                    Mocker.GetMock<IChapterFileService>()
                        .Setup(c => c.GetFilesByChapter(_chapter.Id))
                        .Returns(new List<ChapterFile> { new ChapterFile { Id = 1, Path = _stagingPath } });

                    var results = new List<MangaImportResult>();
                    foreach (var d in decisions)
                    {
                        var cf = new ChapterFile
                        {
                            Id = 1,
                            MangaId = d.LocalChapter.Manga.Id,
                            ChapterId = d.LocalChapter.Chapter.Id,
                            Path = d.LocalChapter.Path
                        };
                        results.Add(new MangaImportResult(d, cf));
                    }

                    return results;
                });

            Mocker.GetMock<IChapterDownloadStateRepository>()
                .Setup(r => r.ByStatus(ChapterDownloadStatus.Completed))
                .Returns(new List<ChapterDownloadState> { _state });

            // Path A (reactive)
            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            // Path B (poll) — same chapter
            Subject.Execute(new ProcessMangaCompletedCommand());

            // Idempotency: only ONE import call total. Pattern 1 contract.
            importCount.Should().Be(1);
        }

        // ── SCRATCH DIR CLEANUP (D-08) ──────────────────────────────────────────────────

        [Test]
        public void On_import_success_scratch_directory_is_deleted()
        {
            Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));

            Mocker.GetMock<IDiskProvider>()
                .Verify(d => d.DeleteFolder(System.IO.Path.GetDirectoryName(_stagingPath), true), Times.Once);
        }

        [Test]
        public void Scratch_dir_delete_failure_does_not_poison_import_result()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()))
                .Throws(new System.IO.IOException("read-only filesystem"));

            // Should NOT throw — the warn log is the contract.
            Action act = () => Subject.Handle(new ChapterArchivedEvent(_manga.Id, _chapter.Id, _stagingPath, "cbz"));
            act.Should().NotThrow();

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), true, null), Times.Once);

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
