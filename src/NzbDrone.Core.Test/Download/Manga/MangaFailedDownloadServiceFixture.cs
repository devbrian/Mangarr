using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 36 Plan 04 Task 2 — MangaFailedDownloadService contract tests.
    //
    //   ProcessFailed(TrackedDownload) blocklists ALWAYS (regardless of AutoRedownloadFailed) and
    //   publishes ChapterDownloadFailedEvent with MangaId/ChapterId re-sourced from the in-memory
    //   RemoteChapter (Landmine #1 — ChapterDownloadState.Id is absent on the generalized path,
    //   so RowId=0). The blocklist + re-search chain stays KEPT: MangaBlocklistService handles
    //   ChapterDownloadFailedEvent → MangaBlocklistAddedEvent → AutoRetryOrchestrator.
    //
    //   Tests cover:
    //     1. ProcessFailed publishes ChapterDownloadFailedEvent with RowId=0 + RemoteChapter ids
    //     2. ProcessFailed never reads ChapterDownloadState.Id (RowId always 0)
    //     3. Provenance (Source/DownloadClient/Release) carried from in-memory RemoteChapter
    //     4. Check transitions Failed/Warning → FailedPending
    [TestFixture]
    public class MangaFailedDownloadServiceFixture : CoreTest<MangaFailedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _trackedDownload = new TrackedDownloadBuilder()
                .WithDownloadId("dl-fail-1")
                .WithChapters(42)
                .Failed()
                .Build();

            _trackedDownload.RemoteChapter.Release.Indexer = "MangaDex";
            _trackedDownload.RemoteChapter.Release.Title = "Test Manga - Chapter 001";
        }

        // ── 1. Blocklist ALWAYS fires via the kept chain (ChapterDownloadFailedEvent published) ──

        [Test]
        public void ProcessFailed_publishes_ChapterDownloadFailedEvent_always()
        {
            Subject.ProcessFailed(_trackedDownload);

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()), Times.Once);
        }

        // ── 2. Landmine #1 — RowId=0, ids from RemoteChapter ────────────────────────────

        [Test]
        public void ProcessFailed_event_carries_RowId_zero_and_RemoteChapter_ids()
        {
            ChapterDownloadFailedEvent captured = null;
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()))
                .Callback<ChapterDownloadFailedEvent>(ev => captured = ev);

            Subject.ProcessFailed(_trackedDownload);

            captured.Should().NotBeNull();
            captured.RowId.Should().Be(0, "Landmine #1: ChapterDownloadState.Id is absent on the generalized path");
            captured.MangaId.Should().Be(_trackedDownload.RemoteChapter.Manga.Id);
            captured.ChapterId.Should().Be(_trackedDownload.RemoteChapter.Chapters[0].Id);
        }

        // ── WR-07 — multi-chapter pack failure blocklists/re-searches EVERY chapter ──────

        [Test]
        public void ProcessFailed_multi_chapter_pack_publishes_an_event_for_every_chapter()
        {
            var pack = new TrackedDownloadBuilder()
                .WithDownloadId("dl-pack-fail")
                .WithChapters(179, 180, 181)
                .Failed()
                .Build();
            pack.RemoteChapter.Release.Indexer = "MangaDex";
            pack.RemoteChapter.Release.Title = "Test Manga - c179-181";

            var failedChapterIds = new System.Collections.Generic.List<int>();
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()))
                .Callback<ChapterDownloadFailedEvent>(ev => failedChapterIds.Add(ev.ChapterId));

            Subject.ProcessFailed(pack);

            failedChapterIds.Should().BeEquivalentTo(new[] { 179, 180, 181 },
                "WR-07: a multi-chapter pack failure must blocklist + auto-retry EVERY chapter, never just the first");

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()), Times.Exactly(3));
        }

        // ── 3. Provenance carried from in-memory RemoteChapter ──────────────────────────

        [Test]
        public void ProcessFailed_event_carries_release_provenance_from_RemoteChapter()
        {
            ChapterDownloadFailedEvent captured = null;
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()))
                .Callback<ChapterDownloadFailedEvent>(ev => captured = ev);

            Subject.ProcessFailed(_trackedDownload);

            captured.Release.Should().BeSameAs(_trackedDownload.RemoteChapter.Release);
            captured.SourceTitle.Should().Be("Test Manga - Chapter 001");
        }

        [Test]
        public void ProcessFailed_with_null_RemoteChapter_does_not_throw_and_does_not_publish()
        {
            var td = new TrackedDownloadBuilder().WithDownloadId("dl-no-chapter").Failed().Build();
            td.RemoteChapter = null;

            System.Action act = () => Subject.ProcessFailed(td);
            act.Should().NotThrow();

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()), Times.Never);
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        // ── 4. Check transition ─────────────────────────────────────────────────────────

        [Test]
        public void Check_transitions_failed_item_to_FailedPending()
        {
            var td = new TrackedDownloadBuilder().WithChapters(42).Failed().Build();

            // Builder already calls Fail() (Error + FailedPending). Reset to verify Check drives it.
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void Check_transitions_warning_item_to_FailedPending()
        {
            var td = new TrackedDownloadBuilder().WithChapters(42).Build();
            td.DownloadItem.Status = DownloadItemStatus.Warning;
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void Check_does_not_transition_ok_item()
        {
            var td = new TrackedDownloadBuilder().WithChapters(42).Build();
            td.DownloadItem.Status = DownloadItemStatus.Downloading;
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void Check_does_not_re_fail_a_row_already_past_downloading()
        {
            // A Failed row reused across polls (monitor registry merge, #301) is fed back through the
            // pipeline; Check must not re-Fail it (which would reset it to FailedPending and re-publish).
            var td = new TrackedDownloadBuilder().WithChapters(42).Failed().Build();
            td.State = TrackedDownloadState.Failed;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Failed);
        }

        // ── #301: ProcessFailed is one-shot — terminal transition + FailedPending guard ──

        [Test]
        public void ProcessFailed_transitions_the_row_to_terminal_Failed()
        {
            // _trackedDownload is built FailedPending (via .Failed()).
            Subject.ProcessFailed(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Failed,
                "#301: processing a failed download must move it to terminal Failed so a reused instance "
                + "is not re-processed on the next poll");
        }

        [Test]
        public void ProcessFailed_is_a_no_op_when_the_row_is_already_Failed()
        {
            _trackedDownload.State = TrackedDownloadState.Failed;

            Subject.ProcessFailed(_trackedDownload);

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()),
                    Times.Never,
                    "#301: an already-Failed row must not re-publish — no duplicate blocklist/history/auto-retry");
        }
    }
}
