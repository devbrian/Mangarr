using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Queue.Manga
{
    // Phase 36 Plan 36-06 (D-01 / D-01a / D-01b / D-02 / LOOP-05) — CONTRACT tests for the ADDITIVE
    // manga-native page-progress channel threaded into the Queue projection. These assert observable
    // behavior, NOT internal structure (the page caption is sourced from the Plan 03 matcher's
    // IMangaTrackedDownloadService.GetPageProgress carrier keyed by DownloadId — NEVER from
    // ChapterDownloadState and NEVER from the shared DownloadClientItem contract).
    //
    // Seven locked behaviors:
    //   1. PAGE-PRESENT — a TrackedDownload whose DownloadId carries 20/7 maps to TotalPages=20,
    //      CompletedPages=7.
    //   2. PAGE-ABSENT (gateway) — a TrackedDownload with no page progress maps to null/null (the
    //      gateway path leaves the caption absent).
    //   3. SOURCE-KEY — the page counts are sourced by the row's DownloadId (the matcher channel),
    //      not by any DownloadClientItem/ChapterDownloadState field.
    //   4. D-01b — the Size/SizeLeft bar-fill mapping is unchanged (byte/% still drives the bar).
    //   5. D-02 — the TimeLeft/EstimatedCompletionTime ETA mapping is unchanged (always-on ETA
    //      preserved; no manga-specific suppression rule).
    //   6. The resource mapper carries the page fields through to the wire (null when absent).
    //   7. The page channel is presentational-only — its presence/absence does not change the
    //      number of rows projected.
    //
    // The Subject is constructed by hand (NOT via Mocker.Resolve) so the
    // IMangaTrackedDownloadService page-channel mock is fully controlled per test.
    [TestFixture]
    public class MangaQueueServicePageChannelFixture : CoreTest
    {
        private Mock<IEventAggregator> _eventAggregator;
        private Mock<IMangaTrackedDownloadService> _trackedDownloadService;

        [SetUp]
        public void Setup()
        {
            _eventAggregator = new Mock<IEventAggregator>();
            _trackedDownloadService = new Mock<IMangaTrackedDownloadService>();

            // Default: no page progress reported (gateway-shape) for any id.
            _trackedDownloadService
                .Setup(s => s.GetPageProgress(It.IsAny<string>()))
                .Returns((MangaDownloadPageProgress)null);
        }

        private MangaQueueService Subject()
        {
            return new MangaQueueService(
                _eventAggregator.Object,
                _trackedDownloadService.Object,
                TestLogger);
        }

        private static TrackedDownload TrackableDownload(string downloadId, params int[] chapterIds)
        {
            var builder = new TrackedDownloadBuilder().WithDownloadId(downloadId);
            if (chapterIds != null && chapterIds.Length > 0)
            {
                builder.WithChapters(chapterIds);
            }

            var tracked = builder.Build();

            // The TrackedDownloadBuilder leaves IsTrackable at its default (false); the projection
            // filters on t.IsTrackable && t.Protocol == Http, so flip it on for the test row.
            tracked.IsTrackable = true;
            return tracked;
        }

        private List<MangaQueueItem> Project(MangaQueueService subject, params TrackedDownload[] tracked)
        {
            subject.Handle(new TrackedDownloadRefreshedEvent(tracked.ToList()));
            return subject.GetMangaQueue();
        }

        // (1) PAGE-PRESENT — a reporting matcher channel surfaces TotalPages=20 / CompletedPages=7.
        [Test]
        public void Page_present_maps_total_and_completed_pages()
        {
            _trackedDownloadService
                .Setup(s => s.GetPageProgress("dl-pages"))
                .Returns(new MangaDownloadPageProgress(20, 7));

            var queue = Project(Subject(), TrackableDownload("dl-pages", 42));

            queue.Should().ContainSingle();
            queue[0].TotalPages.Should().Be(20);
            queue[0].CompletedPages.Should().Be(7);
        }

        // (2) PAGE-ABSENT (gateway) — no page progress → null/null caption (bytes/% fallback).
        [Test]
        public void Page_absent_gateway_shape_maps_null_pages()
        {
            // The default Setup returns null for every id (gateway path).
            var queue = Project(Subject(), TrackableDownload("dl-gateway", 42));

            queue.Should().ContainSingle();
            queue[0].TotalPages.Should().BeNull();
            queue[0].CompletedPages.Should().BeNull();
        }

        // (3) SOURCE-KEY — the page counts are sourced by the row's DownloadId via the matcher
        // channel (IMangaTrackedDownloadService.GetPageProgress), NOT via any other field.
        [Test]
        public void Page_counts_sourced_by_download_id_from_matcher_channel()
        {
            _trackedDownloadService
                .Setup(s => s.GetPageProgress("dl-keyed"))
                .Returns(new MangaDownloadPageProgress(10, 3));

            var queue = Project(Subject(), TrackableDownload("dl-keyed", 42));

            queue.Should().ContainSingle();
            queue[0].TotalPages.Should().Be(10);
            queue[0].CompletedPages.Should().Be(3);

            // The projection asked the matcher channel for this exact DownloadId — confirming the
            // counts come from the Plan 03 carrier, not ChapterDownloadState / DownloadClientItem.
            _trackedDownloadService.Verify(s => s.GetPageProgress("dl-keyed"), Times.AtLeastOnce);
        }

        // (4) D-01b — the Size / SizeLeft bar-fill mapping is unchanged regardless of page progress.
        [Test]
        public void Size_and_size_left_bar_fill_mapping_is_unchanged_when_pages_present()
        {
            _trackedDownloadService
                .Setup(s => s.GetPageProgress("dl-bar"))
                .Returns(new MangaDownloadPageProgress(20, 7));

            var tracked = TrackableDownload("dl-bar", 42);
            var queue = Project(Subject(), tracked);

            queue.Should().ContainSingle();
            queue[0].Size.Should().Be(tracked.DownloadItem.TotalSize);
            queue[0].SizeLeft.Should().Be(tracked.DownloadItem.RemainingSize);
        }

        // (5) D-02 — the TimeLeft / EstimatedCompletionTime ETA mapping is unchanged (always-on ETA;
        // no manga-specific suppression rule). TimeLeft maps straight through; ECT is derived from it.
        [Test]
        public void Eta_mapping_is_unchanged_when_pages_present()
        {
            _trackedDownloadService
                .Setup(s => s.GetPageProgress("dl-eta"))
                .Returns(new MangaDownloadPageProgress(20, 7));

            var tracked = TrackableDownload("dl-eta", 42);

            var before = System.DateTime.UtcNow;
            var queue = Project(Subject(), tracked);
            var after = System.DateTime.UtcNow;

            queue.Should().ContainSingle();
            queue[0].TimeLeft.Should().Be(tracked.DownloadItem.RemainingTime);

            if (tracked.DownloadItem.RemainingTime.HasValue)
            {
                // ECT = UtcNow.Add(TimeLeft) at projection time, so it must fall within
                // [before+RemainingTime, after+RemainingTime] and carry DateTimeKind.Utc — a tighter
                // bound than "not null" that pins both the derivation and the UTC kind.
                var remaining = tracked.DownloadItem.RemainingTime.Value;
                queue[0].EstimatedCompletionTime.Should().NotBeNull();
                queue[0].EstimatedCompletionTime.Value.Kind.Should().Be(System.DateTimeKind.Utc);
                queue[0].EstimatedCompletionTime.Value
                    .Should().BeOnOrAfter(before.Add(remaining))
                    .And.BeOnOrBefore(after.Add(remaining));
            }
            else
            {
                queue[0].EstimatedCompletionTime.Should().BeNull();
            }
        }

        // (6) POCO ADDITIVE FIELDS — the page fields live on the manga-side MangaQueueItem POCO only
        // (the wire DTO MangaQueueResource mirrors them; that mapper is exercised by build + the
        // Api.Test layer — NzbDrone.Core.Test deliberately does NOT project-reference Mangarr.Api.V5
        // per the Plan 10-05 convention). Here we assert the projection's POCO defaults are null
        // (absent) so the gateway path is null-clean end-to-end on the manga-side projection.
        [Test]
        public void Manga_queue_item_page_fields_default_to_null()
        {
            var item = new MangaQueueItem();
            item.TotalPages.Should().BeNull();
            item.CompletedPages.Should().BeNull();
        }

        // (7) PRESENTATIONAL-ONLY — page presence/absence does not change the row count projected
        // (the caption is additive; one row per chapter regardless).
        [Test]
        public void Page_channel_does_not_change_projected_row_count()
        {
            _trackedDownloadService
                .Setup(s => s.GetPageProgress("dl-rows"))
                .Returns(new MangaDownloadPageProgress(20, 7));

            var withPages = Project(Subject(), TrackableDownload("dl-rows", 1, 2, 3));
            var withoutPages = Project(Subject(), TrackableDownload("dl-norows", 1, 2, 3));

            withPages.Should().HaveCount(3);
            withoutPages.Should().HaveCount(3);
        }
    }
}
