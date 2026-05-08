using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.QueueTests.Manga
{
    // Phase 6 D-20 — MangaQueueService projection tests for Plan 06-05.
    //
    // Verifies:
    //   * mixed-Protocol TrackedDownloadRefreshedEvent only projects DownloadProtocol.Http rows
    //   * each refresh raises MangaQueueUpdatedEvent exactly once
    //   * Find / Remove reach the static _queue
    //   * MapQueueItem Id is deterministic across refreshes (same TrackedDownload + chapter)
    //   * one TrackedDownload with N chapters projects N rows (one per chapter)
    [TestFixture]
    public class MangaQueueServiceFixture : CoreTest<MangaQueueService>
    {
        private DownloadClientItem MakeItem(string id, DownloadProtocol protocol, TimeSpan? remainingTime = null)
        {
            var info = Builder<DownloadClientItemClientInfo>.CreateNew()
                .With(v => v.Protocol = protocol)
                .With(v => v.Name = "Mangarr In-Process Downloader")
                .With(v => v.HasPostImportCategory = false)
                .Build();

            return Builder<DownloadClientItem>.CreateNew()
                .With(v => v.DownloadId = id)
                .With(v => v.Title = "Test Manga - 0001")
                .With(v => v.DownloadClientInfo = info)
                .With(v => v.RemainingTime = remainingTime ?? TimeSpan.FromSeconds(10))
                .With(v => v.TotalSize = 1024L * 1024L)
                .With(v => v.RemainingSize = 512L * 1024L)
                .With(v => v.Status = DownloadItemStatus.Downloading)
                .Build();
        }

        private RemoteChapter MakeRemoteChapter(int mangaId, params int[] chapterIds)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = mangaId, Title = "Test Manga" },
                Chapters = chapterIds.Select(id => new NzbDrone.Core.Manga.Chapter
                {
                    Id = id,
                    MangaId = mangaId,
                    ChapterNumber = id,
                    Monitored = true
                }).ToList(),
                Release = new ReleaseInfo
                {
                    Title = "Test Manga - 0001",
                    Guid = $"g-{mangaId}",
                    Indexer = "MangaDex",
                    TranslatedLanguage = "en",
                    ScanlationGroup = "TestGroup"
                }
            };
        }

        private TrackedDownload MakeTrackedDownload(string id, DownloadProtocol protocol, RemoteChapter remoteChapter = null)
        {
            return new TrackedDownload
            {
                IsTrackable = true,
                Protocol = protocol,
                DownloadItem = MakeItem(id, protocol),
                RemoteChapter = remoteChapter
            };
        }

        [Test]
        public void Handle_only_projects_Http_protocol_TrackedDownloads()
        {
            var manga = MakeTrackedDownload("manga-1", DownloadProtocol.Http, MakeRemoteChapter(7, 42));
            var tv = MakeTrackedDownload("tv-1", DownloadProtocol.Unknown);

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga, tv }));

            var queue = Subject.GetMangaQueue();
            queue.Should().HaveCount(1);
            queue[0].DownloadId.Should().Be("manga-1");
            queue[0].Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public void Handle_publishes_MangaQueueUpdatedEvent_exactly_once_per_refresh()
        {
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, MakeRemoteChapter(7, 42));

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaQueueUpdatedEvent>()), Times.Once);
        }

        [Test]
        public void MapQueueItem_Id_is_deterministic_across_refreshes()
        {
            var rc = MakeRemoteChapter(7, 42);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));
            var firstId = Subject.GetMangaQueue().Single().Id;

            // Re-issue the SAME TrackedDownload (same DownloadId / DownloadClient / chapter) →
            // the deterministic hash must yield the same Id so SignalR diffs land on a stable key.
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));
            var secondId = Subject.GetMangaQueue().Single().Id;

            secondId.Should().Be(firstId);
            secondId.Should().BeGreaterThan(0);
        }

        [Test]
        public void Handle_projects_one_row_per_chapter_when_RemoteChapter_has_multiple_chapters()
        {
            var rc = MakeRemoteChapter(7, 42, 43, 44);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            var queue = Subject.GetMangaQueue();
            queue.Should().HaveCount(3);
            queue.Select(q => q.ChapterId).Should().BeEquivalentTo(new int?[] { 42, 43, 44 });
            queue.All(q => q.MangaId == 7).Should().BeTrue();
        }

        [Test]
        public void Handle_emits_one_shell_row_when_RemoteChapter_is_null()
        {
            // Recovery / orphan-import path: TrackedDownload exists with Http protocol but
            // RemoteChapter has not yet been populated (parsing service hasn't caught up).
            // We still surface the row so the UI shows "in-flight" rather than dropping it.
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, remoteChapter: null);

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            var queue = Subject.GetMangaQueue();
            queue.Should().HaveCount(1);
            queue[0].DownloadId.Should().Be("m-1");
            queue[0].MangaId.Should().BeNull();
            queue[0].ChapterId.Should().BeNull();
        }

        [Test]
        public void Find_returns_matching_item_after_refresh()
        {
            var rc = MakeRemoteChapter(7, 42);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            var seeded = Subject.GetMangaQueue().Single();

            var found = Subject.Find(seeded.Id);
            found.Should().NotBeNull();
            found.Id.Should().Be(seeded.Id);
        }

        [Test]
        public void Find_returns_null_when_id_unknown()
        {
            // Empty queue, unknown id → SingleOrDefault returns null. Mirrors TV
            // QueueService.Find at QueueService.cs:38.
            Subject.Find(int.MaxValue).Should().BeNull();
        }

        [Test]
        public void Remove_drops_item_from_static_queue()
        {
            var rc = MakeRemoteChapter(7, 42);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            var seeded = Subject.GetMangaQueue().Single();

            Subject.Remove(seeded.Id);

            Subject.Find(seeded.Id).Should().BeNull();
            Subject.GetMangaQueue().Should().BeEmpty();
        }

        [Test]
        public void Handle_replaces_static_queue_atomically_on_each_refresh()
        {
            // First refresh — m-1 in flight.
            var rc1 = MakeRemoteChapter(7, 42);
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload>
            {
                MakeTrackedDownload("m-1", DownloadProtocol.Http, rc1)
            }));
            Subject.GetMangaQueue().Should().HaveCount(1);

            // Second refresh — m-1 disappeared, m-2 arrived. The whole list is replaced.
            var rc2 = MakeRemoteChapter(8, 43);
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload>
            {
                MakeTrackedDownload("m-2", DownloadProtocol.Http, rc2)
            }));
            var queue = Subject.GetMangaQueue();
            queue.Should().HaveCount(1);
            queue[0].DownloadId.Should().Be("m-2");
        }

        [Test]
        public void Handle_orders_by_RemainingTime_ascending()
        {
            var rcFast = MakeRemoteChapter(7, 42);
            var rcSlow = MakeRemoteChapter(8, 43);

            var fast = new TrackedDownload
            {
                IsTrackable = true,
                Protocol = DownloadProtocol.Http,
                DownloadItem = MakeItem("fast", DownloadProtocol.Http, TimeSpan.FromSeconds(5)),
                RemoteChapter = rcFast
            };
            var slow = new TrackedDownload
            {
                IsTrackable = true,
                Protocol = DownloadProtocol.Http,
                DownloadItem = MakeItem("slow", DownloadProtocol.Http, TimeSpan.FromSeconds(60)),
                RemoteChapter = rcSlow
            };

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { slow, fast }));

            var queue = Subject.GetMangaQueue();
            queue.Should().HaveCount(2);
            queue[0].DownloadId.Should().Be("fast");
            queue[1].DownloadId.Should().Be("slow");
        }

        [Test]
        public void Handle_carries_TranslatedLanguage_and_ScanlationGroup_through_to_queue_item()
        {
            var rc = MakeRemoteChapter(7, 42);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);

            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));

            var item = Subject.GetMangaQueue().Single();
            item.TranslatedLanguage.Should().Be("en");
            item.ScanlationGroup.Should().Be("TestGroup");
            item.Indexer.Should().Be("MangaDex");
        }

        [Test]
        public void Remove_publishes_MangaQueueUpdatedEvent_when_item_removed()
        {
            // Phase 6 Plan 14 — BL-02 / WR-04 mitigation. Remove must fan out via SignalR
            // like Handle does so connected clients see the deletion immediately without
            // waiting for the next TrackedDownloadRefreshedEvent.
            var rc = MakeRemoteChapter(7, 42);
            var manga = MakeTrackedDownload("m-1", DownloadProtocol.Http, rc);
            Subject.Handle(new TrackedDownloadRefreshedEvent(new List<TrackedDownload> { manga }));
            var seeded = Subject.GetMangaQueue().Single();

            // Reset the verification counter — Handle already published once.
            Mocker.GetMock<IEventAggregator>().Invocations.Clear();

            Subject.Remove(seeded.Id);

            Mocker.GetMock<IEventAggregator>()
                .Verify(
                    e => e.PublishEvent(It.IsAny<MangaQueueUpdatedEvent>()),
                    Times.Once,
                    "BL-02/WR-04: Remove must publish MangaQueueUpdatedEvent so SignalR clients see the deletion immediately.");
        }

        [Test]
        public void Remove_does_not_publish_event_when_id_unknown()
        {
            // Phase 6 Plan 14 — BL-02 negative case. Guards against double-publish on
            // no-op Remove call (e.g., concurrent client deletes racing the same id).
            Subject.Remove(int.MaxValue);
            Mocker.GetMock<IEventAggregator>()
                .Verify(e => e.PublishEvent(It.IsAny<MangaQueueUpdatedEvent>()), Times.Never);
        }
    }
}
