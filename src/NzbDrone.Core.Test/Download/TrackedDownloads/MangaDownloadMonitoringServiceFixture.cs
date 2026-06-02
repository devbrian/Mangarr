using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    // Phase 36 Plan 05 Task 1 — MangaDownloadMonitoringService contract tests (LOOP-01 / LOOP-05).
    //
    //   The poll heart + the dead-queue-fix publisher. Six behaviors per the plan:
    //     1. After one Refresh() over a DownloadHandlingEnabled client returning one in-flight item,
    //        exactly one TrackedDownload is registered (via MangaTrackedDownloadService.TrackDownload).
    //     2. TrackedDownloadRefreshedEvent is published Times.Once AND is the LAST publish in Refresh()
    //        — after tracking + the Completed/Failed Checks (ordering asserted via a callback flag).
    //     3. Refresh() pushes ProcessMonitoredMangaDownloadsCommand onto the queue at its tail (Times.Once).
    //     4. A ChapterGrabbedEvent and a ChapterImportedEvent each trigger the 5s debounced Refresh
    //        path (the Debouncer.Execute fires the queued RefreshMonitoredMangaDownloadsCommand; NOT
    //        a direct/immediate Refresh — no publish from the grab/import handler).
    //     5/6. (TaskManager registration is asserted by TaskManagerDefaultTasksFixture, not here.)
    //
    //   Contract tests with the Plan 01 shared builders (TrackedDownloadBuilder / DownloadClientItemBuilder).
    //   Anti-pattern F applied: the TrackedDownloadRefreshedEvent publish is asserted write/track-FIRST,
    //   publish-LAST.
    [TestFixture]
    public class MangaDownloadMonitoringServiceFixture : CoreTest<MangaDownloadMonitoringService>
    {
        private Mock<IDownloadClient> _downloadClient;
        private DownloadClientDefinition _definition;
        private DownloadClientItem _item;
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            _definition = new DownloadClientDefinition
            {
                Id = 1,
                Name = "InProcess",
                Protocol = DownloadProtocol.Http
            };

            _item = new DownloadClientItemBuilder()
                .WithDownloadId("dl-1")
                .Build();

            _trackedDownload = new TrackedDownloadBuilder()
                .WithDownloadId("dl-1")
                .WithChapters(42)
                .Build();   // State == Downloading (so the Checks run)

            _downloadClient = new Mock<IDownloadClient>();
            _downloadClient.SetupGet(c => c.Definition).Returns(_definition);
            _downloadClient.Setup(c => c.GetItems()).Returns(new[] { _item });

            Mocker.GetMock<IDownloadClientFactory>()
                .Setup(f => f.DownloadHandlingEnabled(It.IsAny<bool>()))
                .Returns(new List<IDownloadClient> { _downloadClient.Object });

            Mocker.GetMock<IMangaTrackedDownloadService>()
                .Setup(t => t.TrackDownload(It.IsAny<DownloadClientDefinition>(), It.IsAny<DownloadClientItem>()))
                .Returns(_trackedDownload);
        }

        // ── 1. Exactly one TrackedDownload registered per in-flight item ────────────────

        [Test]
        public void Refresh_tracks_each_in_flight_item_once()
        {
            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IMangaTrackedDownloadService>()
                .Verify(t => t.TrackDownload(_definition, _item), Times.Once);

            Subject.GetTrackedDownloads().Should().ContainSingle()
                .Which.Should().BeSameAs(_trackedDownload);
        }

        // ── 2. TrackedDownloadRefreshedEvent published once AND last (anti-pattern F) ───

        [Test]
        public void Refresh_publishes_TrackedDownloadRefreshedEvent_exactly_once()
        {
            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()), Times.Once);
        }

        [Test]
        public void Refresh_publishes_TrackedDownloadRefreshedEvent_AFTER_tracking_and_checks()
        {
            var trackCalled = false;
            var completedCheckCalled = false;
            var failedCheckCalled = false;
            var publishedAfterAll = false;

            Mocker.GetMock<IMangaTrackedDownloadService>()
                .Setup(t => t.TrackDownload(It.IsAny<DownloadClientDefinition>(), It.IsAny<DownloadClientItem>()))
                .Callback(() => trackCalled = true)
                .Returns(_trackedDownload);

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Setup(c => c.Check(It.IsAny<TrackedDownload>()))
                .Callback(() => completedCheckCalled = true);

            Mocker.GetMock<IMangaFailedDownloadService>()
                .Setup(f => f.Check(It.IsAny<TrackedDownload>()))
                .Callback(() => failedCheckCalled = true);

            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()))
                .Callback(() => publishedAfterAll = trackCalled && completedCheckCalled && failedCheckCalled);

            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            publishedAfterAll.Should().BeTrue(
                "the dead-queue fix (LOOP-05) requires TrackedDownloadRefreshedEvent to be the LAST step — "
                + "after TrackDownload + the Completed/Failed Checks (anti-pattern F: write/track-FIRST, publish-LAST)");
        }

        [Test]
        public void Refresh_carries_the_tracked_downloads_in_the_published_event()
        {
            List<TrackedDownload> published = null;
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()))
                .Callback<TrackedDownloadRefreshedEvent>(e => published = e.TrackedDownloads);

            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            published.Should().ContainSingle().Which.Should().BeSameAs(_trackedDownload);
        }

        // ── 3. ProcessMonitoredMangaDownloadsCommand pushed at the tail ─────────────────

        [Test]
        public void Refresh_pushes_ProcessMonitoredMangaDownloadsCommand_at_its_tail()
        {
            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<ProcessMonitoredMangaDownloadsCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }

        [Test]
        public void Refresh_runs_the_Completed_and_Failed_Checks_for_a_downloading_item()
        {
            Subject.Execute(new RefreshMonitoredMangaDownloadsCommand());

            Mocker.GetMock<IMangaCompletedDownloadService>()
                .Verify(c => c.Check(_trackedDownload), Times.Once);
            Mocker.GetMock<IMangaFailedDownloadService>()
                .Verify(f => f.Check(_trackedDownload), Times.Once);
        }

        // ── 4. Grab / import events trigger the 5s debounced Refresh (NOT an immediate one) ──

        [Test]
        public void ChapterGrabbedEvent_does_not_immediately_Refresh_or_publish()
        {
            Subject.Handle(new ChapterGrabbedEvent(_trackedDownload.RemoteChapter, "dl-1", "InProcess"));

            // The 5s debounce has NOT elapsed — no Refresh side effects fire synchronously.
            Mocker.GetMock<IDownloadClientFactory>()
                .Verify(f => f.DownloadHandlingEnabled(It.IsAny<bool>()), Times.Never);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<TrackedDownloadRefreshedEvent>()), Times.Never);
        }

        [Test]
        public void ChapterGrabbedEvent_eventually_queues_a_RefreshMonitoredMangaDownloadsCommand()
        {
            Subject.Handle(new ChapterGrabbedEvent(_trackedDownload.RemoteChapter, "dl-1", "InProcess"));

            WaitForDebounce();

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<RefreshMonitoredMangaDownloadsCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.AtLeastOnce);
        }

        [Test]
        public void ChapterImportedEvent_eventually_queues_a_RefreshMonitoredMangaDownloadsCommand()
        {
            Subject.Handle(new ChapterImportedEvent());

            WaitForDebounce();

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(q => q.Push(
                        It.IsAny<RefreshMonitoredMangaDownloadsCommand>(),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.AtLeastOnce);
        }

        // The Debouncer is a real 5s timer; poll for the queued Refresh command up to a generous
        // ceiling so the test is not flaky under CI load. The debounce duration is a private
        // implementation detail — we assert eventual dispatch, not exact timing.
        private void WaitForDebounce()
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var fired = Mocker.GetMock<IManageCommandQueue>().Invocations.Any(i =>
                    i.Method.Name == nameof(IManageCommandQueue.Push) &&
                    i.Arguments.Count > 0 &&
                    i.Arguments[0] is RefreshMonitoredMangaDownloadsCommand);

                if (fired)
                {
                    return;
                }

                System.Threading.Thread.Sleep(100);
            }
        }
    }
}
