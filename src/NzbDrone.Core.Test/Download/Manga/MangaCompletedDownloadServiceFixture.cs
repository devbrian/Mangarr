using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 36 Plan 04 Task 1 — MangaCompletedDownloadService contract tests.
    //
    //   The import core was EXTRACTED from the in-process ProcessMangaCompletedDownloads.ProcessOne
    //   (retired in Phase 39 RETIRE-01) and GENERALIZED to the matched TrackedDownload (Plan 03):
    //   staging path ← DownloadItem.OutputPath, chapterIds ← RemoteChapter.Chapters[*].Id,
    //   provenance ← in-memory RemoteChapter.Release (NO ChapterDownloadState JSON round-trip).
    //   Five behaviors per the plan:
    //     1. Non-blank provenance (ScanlationGroup / TranslatedLanguage from RemoteChapter.Release)
    //     2. Single approved import dispatch (IMakeMangaImportDecision → IImportApprovedChapters)
    //     3. Idempotent short-circuit on GetFilesByChapter
    //     4. ChapterDownloadCompletedEvent published AFTER import (Pitfall-4 / anti-pattern F)
    //     5. Check transitions Completed+OutputPath → ImportPending
    [TestFixture]
    public class MangaCompletedDownloadServiceFixture : CoreTest<MangaCompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownloadBuilder()
                .WithDownloadId("dl-1")
                .WithChapters(42)
                .WithLanguage("en")
                .Completed()
                .WithOutputPath(@"C:\staging\Test Manga - Chapter 001\ch001.cbz")
                .Build();

            // Provenance the generalized import core reads from the in-memory RemoteChapter.Release.
            _trackedDownload.RemoteChapter.Release.ScanlationGroup = "Acme Scans";
            _trackedDownload.RemoteChapter.Release.TranslatedLanguage = "en";

            // No existing ChapterFile → import proceeds (idempotency short-circuit not triggered).
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(It.IsAny<int>()))
                .Returns(new List<ChapterFile>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.GetFileSize(It.IsAny<string>()))
                .Returns(2048L);

            // Approved decision by default.
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalChapter, DownloadClientItem>((lc, _) => new MangaImportDecision(lc));

            // Importer returns an Imported result.
            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(),
                                     It.IsAny<bool>(),
                                     It.IsAny<DownloadClientItem>(),
                                     It.IsAny<bool>()))
                .Returns<List<MangaImportDecision>, bool, DownloadClientItem, bool>((decisions, _, _, _) =>
                {
                    var results = new List<MangaImportResult>();
                    foreach (var d in decisions)
                    {
                        results.Add(new MangaImportResult(d, new ChapterFile
                        {
                            Id = 5,
                            MangaId = d.LocalChapter.Manga.Id,
                            ChapterId = d.LocalChapter.Chapter.Id,
                            Path = d.LocalChapter.Path
                        }));
                    }

                    return results;
                });
        }

        // ── 1. Non-blank provenance ─────────────────────────────────────────────────────

        [Test]
        public void Import_builds_LocalChapter_with_non_blank_provenance_from_in_memory_RemoteChapter()
        {
            LocalChapter captured = null;
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Callback<LocalChapter, DownloadClientItem>((lc, _) => captured = lc)
                .Returns<LocalChapter, DownloadClientItem>((lc, _) => new MangaImportDecision(lc));

            Subject.Import(_trackedDownload);

            captured.Should().NotBeNull();
            captured.TranslatedLanguage.Should().Be("en");
            captured.ScanlationGroup.Should().Be("Acme Scans");
            captured.Release.Should().BeSameAs(_trackedDownload.RemoteChapter.Release);
        }

        // ── 2. Single approved import dispatch ──────────────────────────────────────────

        [Test]
        public void Import_dispatches_approved_decision_into_ImportApprovedChapters_once()
        {
            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeTrue("WR-05: a genuine import reports true");

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), true, null, It.IsAny<bool>()), Times.Once);
        }

        [Test]
        public void Import_does_not_dispatch_when_decision_rejected()
        {
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalChapter, DownloadClientItem>((lc, _) =>
                    new MangaImportDecision(lc, new MangaImportRejection(ImportRejectionReason.NotUpgradeAllowed, "rejected")));

            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeFalse("WR-05: a rejected decision reports false so the caller retains the row for retry");

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<bool>()),
                    Times.Never);
        }

        // ── 3. Idempotent short-circuit ─────────────────────────────────────────────────

        [Test]
        public void Import_short_circuits_when_chapter_file_already_exists()
        {
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(42))
                .Returns(new List<ChapterFile> { new ChapterFile { Id = 1, Path = "existing.cbz" } });

            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeTrue(
                "WR-05: an already-imported chapter is legitimately importable/removable — the idempotency short-circuit reports true so the redundant scratch data is evicted");

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<bool>()),
                    Times.Never);
        }

        // P1 — the importer can RETURN Skipped/Rejected (destination exists, missing root folder,
        // move/recycle failure) WITHOUT throwing. Import must honor that and report false so the
        // caller leaves the row ImportPending (not evicted with deleteData:true).
        [Test]
        public void Import_reports_false_when_importer_returns_skipped_P1()
        {
            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(),
                                     It.IsAny<bool>(),
                                     It.IsAny<DownloadClientItem>(),
                                     It.IsAny<bool>()))
                .Returns<List<MangaImportDecision>, bool, DownloadClientItem, bool>((decisions, _, _, _) =>
                {
                    // Errors + an approved decision → MangaImportResultType.Skipped.
                    return decisions
                        .Select(d => new MangaImportResult(d, "destination already exists"))
                        .ToList();
                });

            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeFalse(
                "P1: a Skipped importer result is not a genuine import — the row must be retained for retry, not evicted");

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadCompletedEvent>()), Times.Never);

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Import_reports_false_when_importer_returns_empty_P1()
        {
            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(),
                                     It.IsAny<bool>(),
                                     It.IsAny<DownloadClientItem>(),
                                     It.IsAny<bool>()))
                .Returns(new List<MangaImportResult>());

            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeFalse("P1: an empty importer result imports nothing — report false");

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadCompletedEvent>()), Times.Never);

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        // CR-b — a multi-chapter pack where only the FIRST chapter already has a ChapterFile must
        // NOT short-circuit as fully imported. The all-chapters guard proceeds to import so the
        // not-yet-imported chapters are not silently skipped + evicted.
        [Test]
        public void Import_does_not_short_circuit_multi_chapter_pack_when_only_first_chapter_has_file_CRb()
        {
            var td = new TrackedDownloadBuilder()
                .WithDownloadId("dl-pack")
                .WithChapters(179, 180, 181)
                .WithLanguage("en")
                .Completed()
                .WithOutputPath(@"C:\staging\Pack\pack.cbz")
                .Build();
            td.RemoteChapter.Release.ScanlationGroup = "Acme Scans";
            td.RemoteChapter.Release.TranslatedLanguage = "en";

            // Only chapter 179 already has a file; 180/181 do not.
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(It.IsAny<int>()))
                .Returns(new List<ChapterFile>());
            Mocker.GetMock<IChapterFileService>()
                .Setup(c => c.GetFilesByChapter(179))
                .Returns(new List<ChapterFile> { new ChapterFile { Id = 1, Path = "existing.cbz" } });

            var imported = Subject.Import(td);

            imported.Should().BeTrue("CR-b: with a partial pack the import proceeds and reports its genuine result");

            // The import path WAS taken (not short-circuited) — the importer was invoked.
            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), true, null, It.IsAny<bool>()), Times.Once);
        }

        [Test]
        public void Import_reports_false_when_staging_path_missing_WR05()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(false);

            var imported = Subject.Import(_trackedDownload);

            imported.Should().BeFalse(
                "WR-05: a missing staging path is a short-circuit, not a genuine import — the row must be retained for retry");

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        // ── 4. ChapterDownloadCompletedEvent published AFTER import (Pitfall-4 / anti-pattern F) ──

        [Test]
        public void Import_publishes_ChapterDownloadCompletedEvent_after_import_completes()
        {
            var importCalledFirst = false;
            var eventPublishedAfterImport = false;

            Mocker.GetMock<IImportApprovedChapters>()
                .Setup(i => i.Import(It.IsAny<List<MangaImportDecision>>(),
                                     It.IsAny<bool>(),
                                     It.IsAny<DownloadClientItem>(),
                                     It.IsAny<bool>()))
                .Callback(() => importCalledFirst = true)
                .Returns(new List<MangaImportResult>
                {
                    new MangaImportResult(new MangaImportDecision(new LocalChapter()), new ChapterFile { Id = 1 })
                });

            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<ChapterDownloadCompletedEvent>()))
                .Callback(() => eventPublishedAfterImport = importCalledFirst);

            Subject.Import(_trackedDownload);

            eventPublishedAfterImport.Should().BeTrue(
                "Pitfall-4 ordering: ChapterDownloadCompletedEvent must publish only AFTER IImportApprovedChapters.Import returns");

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.Is<ChapterDownloadCompletedEvent>(c => c.DownloadId == "dl-1")), Times.Once);
        }

        [Test]
        public void Import_does_not_publish_completion_event_when_decision_rejected()
        {
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalChapter, DownloadClientItem>((lc, _) =>
                    new MangaImportDecision(lc, new MangaImportRejection(ImportRejectionReason.NotUpgradeAllowed, "rejected")));

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadCompletedEvent>()), Times.Never);
        }

        // ── #319 — short-circuits surface a Warning on the tracked download (not just the log) ──

        [Test]
        public void Import_warns_tracked_download_when_no_remote_chapter_319()
        {
            _trackedDownload.RemoteChapter = null;

            Subject.Import(_trackedDownload);

            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            _trackedDownload.StatusMessages.Should().NotBeEmpty(
                "#319: a no-RemoteChapter short-circuit must surface on the queue row, not just the server log");

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Import_warns_tracked_download_when_staging_path_missing_319()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(d => d.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.Import(_trackedDownload);

            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            _trackedDownload.StatusMessages.Should().NotBeEmpty();

            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void Import_warns_tracked_download_with_rejection_reason_when_decision_rejected_319()
        {
            Mocker.GetMock<IMakeMangaImportDecision>()
                .Setup(d => d.GetDecision(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Returns<LocalChapter, DownloadClientItem>((lc, _) =>
                    new MangaImportDecision(lc, new MangaImportRejection(ImportRejectionReason.NotUpgradeAllowed, "not an upgrade")));

            Subject.Import(_trackedDownload);

            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Warning);
            _trackedDownload.StatusMessages.Should().Contain(m => m.Messages.Any(x => x.Contains("not an upgrade")),
                "#319: the user should see WHY the import was rejected on the queue row");

            // Rejection is logged at Info, not Warn — no ExpectedWarns needed.
        }

        // ── 5. Check transition ─────────────────────────────────────────────────────────

        [Test]
        public void Check_transitions_completed_item_with_output_path_to_ImportPending()
        {
            var td = new TrackedDownloadBuilder()
                .WithChapters(42)
                .Completed()
                .WithOutputPath(@"C:\staging\x\ch.cbz")
                .Build();
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.ImportPending);
        }

        [Test]
        public void Check_does_not_transition_completed_item_without_output_path()
        {
            var td = new TrackedDownloadBuilder()
                .WithChapters(42)
                .Build();
            td.DownloadItem.Status = DownloadItemStatus.Completed;
            td.DownloadItem.OutputPath = new OsPath(null);
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Downloading);
        }
    }
}
