using System.Collections.Generic;
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
    //   The import core was EXTRACTED from ProcessMangaCompletedDownloads.ProcessOne and
    //   GENERALIZED to the matched TrackedDownload (Plan 03): staging path ← DownloadItem.OutputPath,
    //   chapterIds ← RemoteChapter.Chapters[*].Id, provenance ← in-memory RemoteChapter.Release
    //   (NO ChapterDownloadState JSON round-trip). Six behaviors per the plan:
    //     1. Non-blank provenance (ScanlationGroup / TranslatedLanguage from RemoteChapter.Release)
    //     2. Single approved import dispatch (IMakeMangaImportDecision → IImportApprovedChapters)
    //     3. Idempotent short-circuit on GetFilesByChapter
    //     4. ChapterDownloadCompletedEvent published AFTER import (Pitfall-4 / anti-pattern F)
    //     5. Check transitions Completed+OutputPath → ImportPending
    //     6. (paired ProcessMangaCompletedDownloadsFixture stays green — verified separately)
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
            Subject.Import(_trackedDownload);

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

            Subject.Import(_trackedDownload);

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

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IImportApprovedChapters>()
                .Verify(i => i.Import(It.IsAny<List<MangaImportDecision>>(), It.IsAny<bool>(), It.IsAny<DownloadClientItem>(), It.IsAny<bool>()),
                    Times.Never);
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
