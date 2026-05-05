using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.DecisionEngine.Manga.Aggregators;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Pending.Manga
{
    // Phase 9 D-09-06..08 — service-layer fixture for MangaPendingReleaseService.
    //
    // Coverage matrix:
    //   * 9 IHandle subscriber wiring (one test per event)
    //   * Pitfall 4 ordering: Insert + Delete + state-mutating IHandle (DB write FIRST,
    //     MangaPendingReleasesUpdatedEvent publish LAST) — verified via MockSequence
    //   * 9 public methods (Add / AddMany / GetPending / GetPendingRemoteChapters /
    //     GetPendingQueue / FindPendingQueueItem / RemovePendingQueueItems /
    //     OldestPendingRelease)
    //   * GetDelay KNOWN LIMITATION pin: DelayProfile.GetProtocolDelay(Http) silently
    //     returns UsenetDelay (Open Q §3 — fix deferred to v1.1)
    //
    // Per-plan filter (D-09-12): dotnet test --filter "FullyQualifiedName~MangaPendingReleaseServiceFixture"
    [TestFixture]
    public class MangaPendingReleaseServiceFixture : CoreTest<MangaPendingReleaseService>
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private List<NzbDrone.Core.Manga.Chapter> _chapters;
        private MangaDownloadDecision _approvedDecision;
        private MangaDownloadDecision _rejectedDecision;

        [SetUp]
        public void Setup()
        {
            _manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew()
                .With(s => s.Id = 7)
                .With(s => s.Title = "Test Manga")
                .With(s => s.Tags = new HashSet<int>())
                .Build();

            _chapters = Builder<NzbDrone.Core.Manga.Chapter>.CreateListOfSize(2)
                .All()
                .With(c => c.MangaId = _manga.Id)
                .Build()
                .ToList();
            _chapters[0].Id = 100;
            _chapters[0].ChapterNumber = 1m;
            _chapters[0].TranslatedLanguage = "en";
            _chapters[1].Id = 200;
            _chapters[1].ChapterNumber = 2m;
            _chapters[1].TranslatedLanguage = "en";

            _approvedDecision = BuildDecision(_chapters[0], approved: true);
            _rejectedDecision = BuildDecision(_chapters[1], approved: false);

            // Default: empty repo, empty blocked-indexer set, default delay profile, default config.
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease>());

            Mocker.GetMock<IIndexerStatusService>()
                .Setup(s => s.GetBlockedProviders())
                .Returns(new List<IndexerStatus>());

            Mocker.GetMock<IDelayProfileService>()
                .Setup(s => s.AllForTags(It.IsAny<HashSet<int>>()))
                .Returns(new List<DelayProfile>
                {
                    new DelayProfile { Order = 0, UsenetDelay = 42, TorrentDelay = 21, PreferredProtocol = DownloadProtocol.Http },
                });

            Mocker.GetMock<IDelayProfileService>()
                .Setup(s => s.BestForTags(It.IsAny<HashSet<int>>()))
                .Returns(new DelayProfile { PreferredProtocol = DownloadProtocol.Http });

            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MinimumAge)
                .Returns(0);

            Mocker.GetMock<IConfigService>()
                .SetupGet(c => c.MangaRssSyncInterval)
                .Returns(15);

            Mocker.GetMock<NzbDrone.Core.Manga.IMangaService>()
                .Setup(s => s.GetManga(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids => ids.Select(i => i == _manga.Id ? _manga : null).Where(m => m != null).ToList());

            Mocker.GetMock<IRemoteChapterAggregationService>()
                .Setup(s => s.Augment(It.IsAny<RemoteChapter>()))
                .Returns<RemoteChapter>(rc => rc);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private MangaDownloadDecision BuildDecision(NzbDrone.Core.Manga.Chapter chapter, bool approved)
        {
            var release = Builder<ReleaseInfo>.CreateNew()
                .With(r => r.Title = $"Test Manga - Chapter {chapter.ChapterNumber}")
                .With(r => r.Indexer = "TestIndexer")
                .With(r => r.IndexerId = 1)
                .With(r => r.PublishDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .With(r => r.DownloadProtocol = DownloadProtocol.Http)
                .Build();

            var parsed = new ParsedChapterInfo
            {
                ReleaseTitle = release.Title,
                MangaTitle = _manga.Title,
                ChapterNumbers = new[] { chapter.ChapterNumber },
                TranslatedLanguage = chapter.TranslatedLanguage,
            };

            var remoteChapter = new RemoteChapter
            {
                Manga = _manga,
                ParsedChapterInfo = parsed,
                Release = release,
                Chapters = new List<NzbDrone.Core.Manga.Chapter> { chapter },
            };

            if (approved)
            {
                return new MangaDownloadDecision(remoteChapter);
            }

            return new MangaDownloadDecision(
                remoteChapter,
                new DownloadRejection(DownloadRejectionReason.Unknown, "rejected for test", RejectionType.Permanent));
        }

        private MangaPendingRelease BuildPendingRow(NzbDrone.Core.Manga.Chapter chapter, PendingReleaseReason reason = PendingReleaseReason.Delay, int id = 0)
        {
            return new MangaPendingRelease
            {
                Id = id,
                MangaId = _manga.Id,
                Title = $"Test Manga - Chapter {chapter.ChapterNumber}",
                Added = DateTime.UtcNow.AddHours(-1),
                Reason = reason,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    ReleaseTitle = $"Test Manga - Chapter {chapter.ChapterNumber}",
                    MangaTitle = _manga.Title,
                    ChapterNumbers = new[] { chapter.ChapterNumber },
                    TranslatedLanguage = chapter.TranslatedLanguage,
                },
                Release = new ReleaseInfo
                {
                    Title = $"Test Manga - Chapter {chapter.ChapterNumber}",
                    Indexer = "TestIndexer",
                    IndexerId = 1,
                    PublishDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    DownloadProtocol = DownloadProtocol.Http,
                },
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pitfall 4 ordering tests (DB write FIRST, event LAST)
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Add_should_insert_into_repository_BEFORE_publishing_MangaPendingReleasesUpdatedEvent()
        {
            // MockSequence pins the call order — Insert MUST appear before PublishEvent.
            // Mirrors the recycle-FIRST proven pattern from Plan 09-07 UpgradeChapterFileServiceFixture.
            var sequence = new MockSequence();

            Mocker.GetMock<IMangaPendingReleaseRepository>(MockBehavior.Strict)
                .InSequence(sequence)
                .Setup(r => r.Insert(It.IsAny<MangaPendingRelease>()))
                .Returns<MangaPendingRelease>(p => p);

            Mocker.GetMock<IEventAggregator>(MockBehavior.Strict)
                .InSequence(sequence)
                .Setup(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()));

            // Allow any other calls (UpdatePendingReleases path) but they don't break the sequence
            // because UpdatePendingReleases reads via .All(), not Insert/PublishEvent on the same mocks.
            Mocker.GetMock<IMangaPendingReleaseRepository>().Setup(r => r.All()).Returns(new List<MangaPendingRelease>());

            Subject.Add(_approvedDecision, PendingReleaseReason.Delay);

            // Verify both occurred — sequence will throw if the order was inverted.
            Mocker.GetMock<IMangaPendingReleaseRepository>().Verify(r => r.Insert(It.IsAny<MangaPendingRelease>()), Times.Once);
            Mocker.GetMock<IEventAggregator>().Verify(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()), Times.AtLeastOnce);
        }

        [Test]
        public void RemovePendingQueueItems_should_delete_BEFORE_publishing_event()
        {
            // Setup: one row exists, FindPendingRelease(queueId) will return it.
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.AllByMangaId(_manga.Id))
                .Returns(new List<MangaPendingRelease> { row });

            // Force the static cache to be populated so the subject can find the row.
            Subject.Handle(new ApplicationStartedEvent());

            // Find the synthesized queue id.
            var queueId = Subject.GetPendingQueue().Single().Id;

            // Reset the publish counter so the subsequent Remove publishes are isolated.
            Mocker.GetMock<IEventAggregator>().Invocations.Clear();

            // Sequence: Delete must precede MangaPendingReleasesUpdatedEvent publish.
            var sequence = new MockSequence();
            Mocker.GetMock<IMangaPendingReleaseRepository>().Reset();
            Mocker.GetMock<IMangaPendingReleaseRepository>().Setup(r => r.All()).Returns(new List<MangaPendingRelease> { row });
            Mocker.GetMock<IMangaPendingReleaseRepository>().Setup(r => r.AllByMangaId(_manga.Id)).Returns(new List<MangaPendingRelease> { row });

            Mocker.GetMock<IMangaPendingReleaseRepository>(MockBehavior.Strict)
                .InSequence(sequence)
                .Setup(r => r.Delete(row));
            Mocker.GetMock<IEventAggregator>(MockBehavior.Strict)
                .InSequence(sequence)
                .Setup(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()));

            Subject.RemovePendingQueueItems(queueId);

            Mocker.GetMock<IMangaPendingReleaseRepository>().Verify(r => r.Delete(row), Times.AtLeastOnce);
            Mocker.GetMock<IEventAggregator>().Verify(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()), Times.AtLeastOnce);
        }

        // ─────────────────────────────────────────────────────────────────────
        // 9 IHandle subscriber tests (D-09-07 + Open Q §1 9th)
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Handle_MangaEditedEvent_should_call_UpdatePendingReleases()
        {
            Subject.Handle(new MangaEditedEvent(_manga, _manga));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_MangaUpdatedEvent_should_call_UpdatePendingReleases()
        {
            Subject.Handle(new MangaUpdatedEvent(_manga));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_MangaDeletedEvent_should_call_DeleteByMangaIds_AND_publish_event()
        {
            Subject.Handle(new MangaDeletedEvent(_manga, deleteFiles: false));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.DeleteByMangaIds(It.Is<List<int>>(ids => ids.Count == 1 && ids[0] == _manga.Id)), Times.Once);

            Mocker.GetMock<IEventAggregator>()
                .Verify(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_ChapterGrabbedEvent_should_RemoveGrabbed_AND_publish_event()
        {
            // Pre-load static cache with one pending release for the same chapter.
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Subject.Handle(new ApplicationStartedEvent());     // primes _pendingReleases

            // The grabbed RemoteChapter intersects _chapters[0].
            var grabbedRemote = new RemoteChapter
            {
                Manga = _manga,
                Chapters = new List<NzbDrone.Core.Manga.Chapter> { _chapters[0] },
                Release = new ReleaseInfo { Title = "doesnt-matter" },
            };

            Mocker.GetMock<IEventAggregator>().Invocations.Clear();

            Subject.Handle(new ChapterGrabbedEvent(grabbedRemote, "dl-id", "dl-client"));

            // Delete should fire (intersect-and-Delete) and event should publish.
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.Delete(It.IsAny<MangaPendingRelease>()), Times.AtLeastOnce);

            Mocker.GetMock<IEventAggregator>()
                .Verify(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_MangaRssSyncCompleteEvent_should_RemoveRejected_AND_publish_event()
        {
            // Pre-load static cache with a row whose release matches the rejected decision.
            var row = BuildPendingRow(_chapters[1], PendingReleaseReason.Delay, id: 2);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Subject.Handle(new ApplicationStartedEvent());     // primes _pendingReleases

            // The flat List<MangaDownloadDecision> payload (Plan 09-12 close-out) — service
            // filters via .Where(d => d.Rejected) inside the handler.
            var processed = new List<MangaDownloadDecision> { _rejectedDecision, _approvedDecision };

            Mocker.GetMock<IEventAggregator>().Invocations.Clear();

            Subject.Handle(new MangaRssSyncCompleteEvent(processed));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.Delete(It.IsAny<MangaPendingRelease>()), Times.AtLeastOnce);

            Mocker.GetMock<IEventAggregator>()
                .Verify(a => a.PublishEvent(It.IsAny<MangaPendingReleasesUpdatedEvent>()), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_CustomFormatProfileUpdatedEvent_should_call_UpdatePendingReleases()
        {
            Subject.Handle(new CustomFormatProfileUpdatedEvent(1));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_TranslationProfileUpdatedEvent_should_call_UpdatePendingReleases()
        {
            // 9th IHandle — Open Q §1 acceptance.
            Subject.Handle(new TranslationProfileUpdatedEvent(1));

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_ConfigSavedEvent_should_call_UpdatePendingReleases()
        {
            Subject.Handle(new ConfigSavedEvent());

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        [Test]
        public void Handle_ApplicationStartedEvent_should_call_UpdatePendingReleases()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Verify(r => r.All(), Times.AtLeastOnce);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Public method behavior tests
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void GetPending_should_return_ReleaseInfo_for_all_repository_rows_when_no_indexer_blocked()
        {
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            var result = Subject.GetPending();

            result.Should().HaveCount(1);
            result[0].Title.Should().Be(row.Release.Title);
            result[0].PendingReleaseReason.Should().Be(PendingReleaseReason.Delay);
        }

        [Test]
        public void GetPending_should_filter_blocked_indexer_releases()
        {
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Mocker.GetMock<IIndexerStatusService>()
                .Setup(s => s.GetBlockedProviders())
                .Returns(new List<IndexerStatus> { new IndexerStatus { ProviderId = 1, DisabledTill = DateTime.UtcNow.AddHours(2) } });

            var result = Subject.GetPending();

            result.Should().BeEmpty();
        }

        [Test]
        public void GetPendingRemoteChapters_should_filter_by_mangaId()
        {
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Subject.Handle(new ApplicationStartedEvent());     // primes _pendingReleases

            var hits = Subject.GetPendingRemoteChapters(_manga.Id);
            var misses = Subject.GetPendingRemoteChapters(_manga.Id + 999);

            hits.Should().HaveCount(1);
            hits[0].Manga.Id.Should().Be(_manga.Id);
            misses.Should().BeEmpty();
        }

        [Test]
        public void GetPendingQueue_should_project_to_MangaQueueItem_and_skip_Fallback_rows()
        {
            var visible = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            var hidden = BuildPendingRow(_chapters[1], PendingReleaseReason.Fallback, id: 2);

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { visible, hidden });

            Subject.Handle(new ApplicationStartedEvent());

            var result = Subject.GetPendingQueue();

            result.Should().HaveCount(1);
            result[0].MangaId.Should().Be(_manga.Id);
            result[0].Status.Should().Be(PendingReleaseReason.Delay.ToString());
            result[0].Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public void FindPendingQueueItem_should_return_matching_queueId()
        {
            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Subject.Handle(new ApplicationStartedEvent());

            var queueId = Subject.GetPendingQueue().Single().Id;
            var hit = Subject.FindPendingQueueItem(queueId);
            var miss = Subject.FindPendingQueueItem(queueId + 1);

            hit.Should().NotBeNull();
            hit.Id.Should().Be(queueId);
            miss.Should().BeNull();
        }

        [Test]
        public void OldestPendingRelease_should_return_RemoteChapter_with_greatest_AgeHours_for_supplied_chapterIds()
        {
            var older = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            older.Release.PublishDate = DateTime.UtcNow.AddDays(-7);     // bigger AgeHours

            var newer = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 2);
            newer.Release.PublishDate = DateTime.UtcNow.AddDays(-1);     // smaller AgeHours

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { older, newer });

            Subject.Handle(new ApplicationStartedEvent());

            var oldest = Subject.OldestPendingRelease(_manga.Id, new[] { _chapters[0].Id });

            oldest.Should().NotBeNull();

            // The older row (greater AgeHours) should be returned via MaxBy(AgeHours).
            oldest.Release.PublishDate.Should().Be(older.Release.PublishDate);
        }

        // ─────────────────────────────────────────────────────────────────────
        // GetDelay KNOWN LIMITATION pin (Open Q §3 — DelayProfile.GetProtocolDelay(Http)
        // returns UsenetDelay; deferred to v1.1).
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void GetPendingQueue_uses_UsenetDelay_for_Http_releases_per_KnownLimitation_OpenQ3()
        {
            // Arrange: profile with distinct Usenet vs Torrent values so we can detect which one
            // bleeds through. Http should silently take the UsenetDelay path per the GetDelay
            // body's KNOWN LIMITATION comment.
            Mocker.GetMock<IDelayProfileService>()
                .Setup(s => s.AllForTags(It.IsAny<HashSet<int>>()))
                .Returns(new List<DelayProfile>
                {
                    new DelayProfile { Order = 0, UsenetDelay = 999, TorrentDelay = 111, PreferredProtocol = DownloadProtocol.Http },
                });

            var row = BuildPendingRow(_chapters[0], PendingReleaseReason.Delay, id: 1);
            row.Release.PublishDate = DateTime.UtcNow;     // start-of-cooldown reference

            Mocker.GetMock<IMangaPendingReleaseRepository>()
                .Setup(r => r.All())
                .Returns(new List<MangaPendingRelease> { row });

            Subject.Handle(new ApplicationStartedEvent());

            // Act
            var queue = Subject.GetPendingQueue();

            // Assert: the projected EstimatedCompletionTime must reflect the UsenetDelay (999 min)
            // not the TorrentDelay (111 min). This pins the v1.1 fix target — when DelayProfile
            // gains an explicit HttpDelay column, this test will need to be updated.
            queue.Should().HaveCount(1);
            var item = queue.Single();
            item.EstimatedCompletionTime.Should().NotBeNull();
            var deltaMinutes = (item.EstimatedCompletionTime.Value - DateTime.UtcNow).TotalMinutes;
            deltaMinutes.Should().BeGreaterThan(900,
                because: "GetDelay should silently use UsenetDelay (999) per Open Q §3 KNOWN LIMITATION; v1.1 fix will surface HttpDelay separately");
        }
    }
}
