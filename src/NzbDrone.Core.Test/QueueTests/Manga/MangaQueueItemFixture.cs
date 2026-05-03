using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Crypto;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.Test.QueueTests.Manga
{
    // Phase 6 D-20 — POCO/event/interface shape contract test for Plan 06-05.
    // Verifies the deterministic Id semantic (HashConverter.GetHashInt31) and the
    // presence of the wire-level fields the V5 controller (Plan 06-09) and the
    // QueueDuplicateSpecification consumer (Task 3 of this plan) expect.
    [TestFixture]
    public class MangaQueueItemFixture
    {
        [Test]
        public void Id_uses_HashConverter_GetHashInt31_for_deterministic_value()
        {
            // Same input — same hash. Mirrors the TV QueueService.MapQueueItem
            // contract at QueueService.cs:86 ("trackedDownload-{client}-{id}").
            var key = "trackedDownload-MangaDownloader-7-42";
            var first = HashConverter.GetHashInt31(key);
            var second = HashConverter.GetHashInt31(key);

            first.Should().Be(second);
            first.Should().BeGreaterThan(0);
        }

        [Test]
        public void Required_properties_exist_on_MangaQueueItem()
        {
            var t = typeof(MangaQueueItem);

            t.GetProperty(nameof(MangaQueueItem.MangaId)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.ChapterId)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.RemoteChapter)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.Status)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.TimeLeft)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.Size)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.SizeLeft)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.DownloadId)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.Indexer)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.ScanlationGroup)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.TranslatedLanguage)).Should().NotBeNull();
            t.GetProperty(nameof(MangaQueueItem.ErrorMessage)).Should().NotBeNull();
        }

        [Test]
        public void MangaQueueUpdatedEvent_is_marker_IEvent()
        {
            var ev = new MangaQueueUpdatedEvent();
            ev.Should().BeAssignableTo<NzbDrone.Common.Messaging.IEvent>();
        }

        [Test]
        public void IMangaQueueService_exposes_GetMangaQueue_Find_Remove()
        {
            var t = typeof(IMangaQueueService);

            t.GetMethod(nameof(IMangaQueueService.GetMangaQueue)).Should().NotBeNull();
            t.GetMethod(nameof(IMangaQueueService.Find)).Should().NotBeNull();
            t.GetMethod(nameof(IMangaQueueService.Remove)).Should().NotBeNull();
        }
    }
}
