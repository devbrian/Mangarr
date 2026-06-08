using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Manga
{
    // Phase 36 Plan 04 Task 2 — MangaFailedDownloadService contract tests.
    // quick-task 260607-tjn (Fix A) — restored the Sonarr grabbed-history-by-DownloadId correlation
    // gate + already-imported reconciliation (see .planning/debug/chapter-status-stale-failed.md).
    //
    //   ProcessFailed(TrackedDownload) blocklists (regardless of AutoRedownloadFailed) and publishes
    //   ChapterDownloadFailedEvent ONLY when the download correlates to an outstanding Grabbed
    //   ChapterHistory row for its DownloadId AND that grab is not already reconciled to Imported.
    //   MangaId/ChapterId are re-sourced from the in-memory RemoteChapter (Landmine #1 — RowId=0).
    //   The blocklist + re-search chain stays KEPT: MangaBlocklistService handles
    //   ChapterDownloadFailedEvent → MangaBlocklistAddedEvent → AutoRetryOrchestrator.
    //
    //   Test-double discipline (CodeRabbit PR #343): the IChapterHistoryService doubles are bound to
    //   the EXACT DownloadId under test (NOT It.IsAny<string>()), so the tests genuinely verify the
    //   gate's DownloadId correlation — a download whose id is not explicitly seeded falls through to
    //   the SetUp catch-all (empty grabbed history) and bails, which is precisely the contract.
    //
    //   Tests cover:
    //     1. ProcessFailed publishes ChapterDownloadFailedEvent (grabbed + not-imported) with RowId=0
    //     2. ProcessFailed never reads ChapterDownloadState.Id (RowId always 0)
    //     3. Provenance (Source/DownloadClient/Release) carried from in-memory RemoteChapter
    //     4. Check transitions Failed/Warning → FailedPending (grabbed + not-imported)
    //     5. Grabbed-history bail (incl. exact-DownloadId correlation) + already-imported reconciliation
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

            // Catch-all default: a DownloadId that is NOT explicitly seeded has no grabbed history, so
            // the gate bails. This is what makes the exact-DownloadId correlation observable — only the
            // ids passed to SeedGrabbed(...) get an outstanding grab. (Moq matches the LAST matching
            // setup, so the per-id SeedGrabbed calls below win over this catch-all for their own id.)
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.Find(It.IsAny<string>(), ChapterHistoryEventType.Grabbed))
                .Returns(new List<ChapterHistory>());
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<ChapterHistory>());

            // Intended path for _trackedDownload: an outstanding Grabbed row for its DownloadId, NOT yet
            // reconciled to Imported. The original contract tests all run this happy path; the
            // grabbed-bail / already-imported tests override per-test (last Moq setup wins).
            SeedGrabbed("dl-fail-1", 42);
        }

        // Bind the grabbed-history doubles to the EXACT DownloadId so the tests verify the gate's
        // DownloadId correlation (not just "some grab exists"). Find(downloadId, Grabbed) and
        // FindByDownloadId(downloadId) return a Grabbed-only list → the gate passes and
        // IsAlreadyImported is false (last event per chapter == Grabbed).
        private void SeedGrabbed(string downloadId, params int[] chapterIds)
        {
            var grabbed = chapterIds
                .Select(id => new ChapterHistory
                {
                    ChapterId = id,
                    EventType = ChapterHistoryEventType.Grabbed,
                    Date = DateTime.UtcNow.AddMinutes(-10)
                })
                .ToList();

            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.Find(downloadId, ChapterHistoryEventType.Grabbed))
                .Returns(grabbed);

            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.FindByDownloadId(downloadId))
                .Returns(grabbed);
        }

        // ── 1. Blocklist fires via the kept chain when grabbed + not already imported ──

        [Test]
        public void ProcessFailed_publishes_ChapterDownloadFailedEvent_when_grabbed_and_not_imported()
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

            SeedGrabbed("dl-pack-fail", 179, 180, 181);

            var failedChapterIds = new List<int>();
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

            // "dl-no-chapter" is unseeded → the grabbed-empty bail logs one Warn before the
            // RemoteChapter-null guard is ever reached.
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        // ── 4. Check transition ─────────────────────────────────────────────────────────

        [Test]
        public void Check_transitions_failed_item_to_FailedPending()
        {
            var td = new TrackedDownloadBuilder().WithDownloadId("dl-check-1").WithChapters(42).Failed().Build();
            SeedGrabbed("dl-check-1", 42);

            // Builder already calls Fail() (Error + FailedPending). Reset to verify Check drives it.
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.FailedPending);
        }

        [Test]
        public void Check_transitions_warning_item_to_FailedPending()
        {
            var td = new TrackedDownloadBuilder().WithDownloadId("dl-check-2").WithChapters(42).Build();
            SeedGrabbed("dl-check-2", 42);
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

        // ── 5. Grabbed-history gate + already-imported reconciliation (Fix A) ─────────────

        [Test]
        public void Check_bails_when_no_grabbed_history_for_downloadId()
        {
            // No outstanding Grabbed row for this (unseeded) DownloadId → Warn + return WITHOUT Fail()
            // (stays Downloading). Sonarr FailedDownloadService.Check grabbedItems.Empty() bail.
            var td = new TrackedDownloadBuilder().WithDownloadId("dl-ungrabbed").WithChapters(42).Failed().Build();
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Downloading,
                "an un-grabbed failed download must NOT transition to FailedPending (Fail() never reached)");
            td.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void Check_bails_when_grabbed_history_exists_only_for_a_different_downloadId()
        {
            // SetUp seeds a Grabbed row for "dl-fail-1" only. This download has a DIFFERENT id, so the
            // gate (which keys on THIS download's exact DownloadId) finds no matching grab and bails.
            // This is the exact-DownloadId correlation contract: a grab for another job must NOT satisfy it.
            var td = new TrackedDownloadBuilder().WithDownloadId("dl-other-job").WithChapters(42).Failed().Build();
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Downloading,
                "the grabbed-history gate must correlate on THIS download's exact DownloadId, "
                + "not be satisfied by a grab recorded for a different job");
            td.Status.Should().Be(TrackedDownloadStatus.Warning);
        }

        [Test]
        public void Check_skips_already_imported_chapter_same_jobId()
        {
            // A same-jobId gateway re-list of an already-imported chapter: the Grabbed row for this
            // DownloadId still exists (so the grabbed gate passes), BUT it is already reconciled to a
            // newer Imported event → IsAlreadyImported → must NOT re-Fail.
            SeedGrabbed("dl-imported", 42);
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.FindByDownloadId("dl-imported"))
                .Returns(new List<ChapterHistory>
                {
                    new() { ChapterId = 42, EventType = ChapterHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddHours(-6) },
                    new() { ChapterId = 42, EventType = ChapterHistoryEventType.Imported, Date = DateTime.UtcNow.AddHours(-5) }
                });

            var td = new TrackedDownloadBuilder().WithDownloadId("dl-imported").WithChapters(42).Failed().Build();
            td.State = TrackedDownloadState.Downloading;

            Subject.Check(td);

            td.State.Should().Be(TrackedDownloadState.Downloading,
                "an already-imported chapter (grab reconciled to Imported) must NOT re-transition to FailedPending");
        }

        [Test]
        public void ProcessFailed_bails_when_no_grabbed_history()
        {
            // Override _trackedDownload's seeded grab with an empty result for its id.
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.Find("dl-fail-1", ChapterHistoryEventType.Grabbed))
                .Returns(new List<ChapterHistory>());

            // _trackedDownload is FailedPending (built via .Failed()).
            Subject.ProcessFailed(_trackedDownload);

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()),
                    Times.Never,
                    "an un-grabbed failed download must NOT blocklist/auto-retry");
            _trackedDownload.State.Should().Be(TrackedDownloadState.Failed,
                "#301 terminal transition is preserved even when the grabbed gate bails");

            // ProcessFailed's grabbed-empty bail logs via NLog (unlike Check's TrackedDownload.Warn).
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void ProcessFailed_skips_already_imported_chapters()
        {
            // Grabbed gate passes for "dl-fail-1" (SetUp), but the grab is reconciled to Imported.
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.FindByDownloadId("dl-fail-1"))
                .Returns(new List<ChapterHistory>
                {
                    new() { ChapterId = 42, EventType = ChapterHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddHours(-6) },
                    new() { ChapterId = 42, EventType = ChapterHistoryEventType.Imported, Date = DateTime.UtcNow.AddHours(-5) }
                });

            Subject.ProcessFailed(_trackedDownload);

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()),
                    Times.Never,
                    "an already-imported chapter (grab reconciled to Imported) must NOT blocklist/auto-retry");
        }

        [Test]
        public void ProcessFailed_pack_with_one_chapter_not_imported_still_fails_all()
        {
            var pack = new TrackedDownloadBuilder()
                .WithDownloadId("dl-pack-mixed")
                .WithChapters(179, 180, 181)
                .Failed()
                .Build();
            pack.RemoteChapter.Release.Indexer = "MangaDex";
            pack.RemoteChapter.Release.Title = "Test Manga - c179-181";

            // Grabbed for all three (gate passes); Imported for ONLY 179. IsAlreadyImported requires
            // ALL chapters reconciled → false → every chapter still blocklists + auto-retries.
            SeedGrabbed("dl-pack-mixed", 179, 180, 181);
            Mocker.GetMock<IChapterHistoryService>()
                .Setup(s => s.FindByDownloadId("dl-pack-mixed"))
                .Returns(new List<ChapterHistory>
                {
                    new() { ChapterId = 179, EventType = ChapterHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddHours(-6) },
                    new() { ChapterId = 179, EventType = ChapterHistoryEventType.Imported, Date = DateTime.UtcNow.AddHours(-5) },
                    new() { ChapterId = 180, EventType = ChapterHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddHours(-6) },
                    new() { ChapterId = 181, EventType = ChapterHistoryEventType.Grabbed, Date = DateTime.UtcNow.AddHours(-6) }
                });

            Subject.ProcessFailed(pack);

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<ChapterDownloadFailedEvent>()),
                    Times.Exactly(3),
                    "WR-07: a pack where only one chapter imported must still blocklist + auto-retry every chapter");
        }
    }
}
