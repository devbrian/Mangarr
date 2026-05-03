using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-20 STUB-replacement target.
    // Wired by Plan 06-05 (MangaQueueService) + the QueueDuplicateSpecification STUB body
    // replacement that calls _mangaQueueService.GetMangaQueue.
    //
    // Pitfall 6 GUARD: this fixture instantiates QueueDuplicateSpecification with a mock
    // IMangaQueueService directly. The Wave 5 F-01 BLOCKING fixture (Plan 06-12) is the
    // real-DI consumer that proves the auto-discovery 11-spec count remains stable.
    [TestFixture]
    public class QueueDuplicateSpecificationFixture : MangaPipelineTestBase
    {
        private QueueDuplicateSpecification _spec;
        private Mock<IMangaQueueService> _queueService;

        [SetUp]
        public void Setup()
        {
            _queueService = new Mock<IMangaQueueService>();
            _spec = new QueueDuplicateSpecification(_queueService.Object, LogManager.GetLogger("test"));
        }

        private RemoteChapter BuildRemoteChapter(int mangaId = 7, params int[] chapterIds)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = mangaId, Title = "Test Manga" },
                Chapters = (chapterIds.Length == 0 ? new[] { 42 } : chapterIds)
                    .Select(id => new Chapter
                    {
                        Id = id,
                        MangaId = mangaId,
                        ChapterNumber = id,
                        Monitored = true
                    }).ToList(),
                Release = new ReleaseInfo
                {
                    Title = "Test Manga - 0001",
                    Indexer = "MangaDex",
                    Guid = "g1"
                }
            };
        }

        private MangaQueueItem BuildQueueItem(int mangaId, params int[] chapterIds)
        {
            var rc = BuildRemoteChapter(mangaId, chapterIds);
            return new MangaQueueItem
            {
                Id = 1234,
                MangaId = mangaId,
                ChapterId = chapterIds.Length > 0 ? chapterIds[0] : (int?)null,
                RemoteChapter = rc,
                DownloadId = "dl-1"
            };
        }

        [Test]
        public void Rejects_when_in_queue()
        {
            // Queued item shares chapterId 42 with the subject — must reject.
            _queueService.Setup(s => s.GetMangaQueue())
                .Returns(new List<MangaQueueItem> { BuildQueueItem(7, 42) });

            var subject = BuildRemoteChapter(7, 42);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ChapterAlreadyQueued);
            _queueService.Verify(s => s.GetMangaQueue(), Times.Once);
        }

        [Test]
        public void Accepts_when_queue_empty()
        {
            _queueService.Setup(s => s.GetMangaQueue()).Returns(new List<MangaQueueItem>());

            var subject = BuildRemoteChapter(7, 42);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
            _queueService.Verify(s => s.GetMangaQueue(), Times.Once);
        }

        [Test]
        public void Accepts_when_queue_has_unrelated_chapters()
        {
            // Queued item is for a different chapter (43) — subject (42) is fine to grab.
            _queueService.Setup(s => s.GetMangaQueue())
                .Returns(new List<MangaQueueItem> { BuildQueueItem(7, 43) });

            var subject = BuildRemoteChapter(7, 42);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void Rejects_when_any_subject_chapter_overlaps_queued_chapter()
        {
            // Subject release covers chapters 42, 43, 44; queued release covers 43.
            // Overlap on 43 must reject — Sonarr precedent reject-on-any-intersection.
            _queueService.Setup(s => s.GetMangaQueue())
                .Returns(new List<MangaQueueItem> { BuildQueueItem(7, 43) });

            var subject = BuildRemoteChapter(7, 42, 43, 44);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ChapterAlreadyQueued);
        }

        [Test]
        public void Tolerates_queue_item_with_null_RemoteChapter()
        {
            // Orphan/recovery shell row (RemoteChapter null) must NOT crash the spec
            // and must NOT match anything — the subject is unrelated to it.
            _queueService.Setup(s => s.GetMangaQueue())
                .Returns(new List<MangaQueueItem>
                {
                    new MangaQueueItem { Id = 1, RemoteChapter = null, DownloadId = "shell" }
                });

            var subject = BuildRemoteChapter(7, 42);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }
    }
}
