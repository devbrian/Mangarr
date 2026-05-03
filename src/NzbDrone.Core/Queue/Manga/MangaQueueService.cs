using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Queue.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-20 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Queue/QueueService.cs lines 21–106.
    //
    // Q-3 RESEARCH lock: static-list pattern; populated on TrackedDownloadRefreshedEvent
    // (verbatim TV shape); filter to Protocol == DownloadProtocol.Http (manga-side gate
    // — TV QueueService runs on the same event but only the non-Http entries land in its
    // _queue, manga-only entries land here, so the two queues co-exist without double-
    // counting per Plan 09 V5 verification).
    //
    // Manga sibling diverges (D-20):
    //   * Filter Protocol == DownloadProtocol.Http (manga-only)
    //   * MapQueueItem reads RemoteChapter (Phase 5 type), not RemoteEpisode
    //   * Emits MangaQueueUpdatedEvent (Plan 06-09 SignalR), not QueueUpdatedEvent
    //   * No QualityModel / Languages — TranslationProfile + ScanlationGroup are
    //     first-class fields on MangaQueueItem
    //
    // Phase 8 cleanup: collapse with QueueService when Tv/ deletes.
    public class MangaQueueService : IMangaQueueService, IHandle<TrackedDownloadRefreshedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        // Static-list pattern verbatim from TV QueueService.cs:24 — the queue is a
        // process-wide projection, refreshed atomically per TrackedDownloadRefreshedEvent.
        // T-06-10 mitigation: single-writer (Handle) — Sonarr precedent.
        private static List<MangaQueueItem> _queue = new();

        public MangaQueueService(IEventAggregator eventAggregator, Logger logger)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public List<MangaQueueItem> GetMangaQueue()
        {
            // Defensive copy — callers (QueueDuplicateSpecification, V5 controller) may
            // iterate while a fresh Handle(...) refresh is in flight.
            return _queue.ToList();
        }

        public MangaQueueItem Find(int id)
        {
            return _queue.SingleOrDefault(q => q.Id == id);
        }

        public void Remove(int id)
        {
            var item = Find(id);
            if (item != null)
            {
                _queue.Remove(item);
            }
        }

        public void Handle(TrackedDownloadRefreshedEvent message)
        {
            _queue = message.TrackedDownloads
                .Where(t => t.IsTrackable && t.Protocol == DownloadProtocol.Http)
                .OrderBy(c => c.DownloadItem?.RemainingTime ?? TimeSpan.MaxValue)
                .SelectMany(MapQueueItems)
                .ToList();

            _eventAggregator.PublishEvent(new MangaQueueUpdatedEvent());
        }

        // RemoteChapter is set on TrackedDownload by Phase 4's InProcessImageDownloadClient
        // (Phase 6 added the slot — see TrackedDownload.cs Sonarr-divergence comment).
        // When the projection runs without a populated RemoteChapter (e.g., orphan import
        // recovery before the parsing service catches up), we still emit a single shell
        // queue row so the UI shows "1 download in flight" rather than dropping the entry.
        private IEnumerable<MangaQueueItem> MapQueueItems(TrackedDownload trackedDownload)
        {
            var remoteChapter = trackedDownload.RemoteChapter;
            if (remoteChapter == null || remoteChapter.Chapters == null || remoteChapter.Chapters.Count == 0)
            {
                yield return MapQueueItem(trackedDownload, null);
                yield break;
            }

            foreach (var chapter in remoteChapter.Chapters)
            {
                yield return MapQueueItem(trackedDownload, chapter);
            }
        }

        private MangaQueueItem MapQueueItem(TrackedDownload td, NzbDrone.Core.Manga.Chapter chapter)
        {
            var rc = td.RemoteChapter;
            var item = new MangaQueueItem
            {
                MangaId = rc?.Manga?.Id,
                ChapterId = chapter?.Id,
                Manga = rc?.Manga,
                Chapter = chapter,
                Chapters = rc?.Chapters?.ToList() ?? new List<NzbDrone.Core.Manga.Chapter>(),
                RemoteChapter = rc,
                TranslatedLanguage = rc?.Release?.TranslatedLanguage,
                ScanlationGroup = rc?.Release?.ScanlationGroup,
                Size = td.DownloadItem?.TotalSize ?? 0,
                Title = td.DownloadItem?.Title,
                SizeLeft = td.DownloadItem?.RemainingSize ?? 0,
                TimeLeft = td.DownloadItem?.RemainingTime,
                Status = td.DownloadItem?.Status.ToString(),
                TrackedDownloadStatus = td.Status.ToString(),
                TrackedDownloadState = td.State.ToString(),
                StatusMessages = td.StatusMessages?.ToList() ?? new List<TrackedDownloadStatusMessage>(),
                ErrorMessage = td.DownloadItem?.Message,
                DownloadId = td.DownloadItem?.DownloadId,
                Indexer = rc?.Release?.Indexer ?? td.Indexer,
                OutputPath = td.DownloadItem?.OutputPath.ToString(),
                Protocol = td.Protocol,
                DownloadClient = td.DownloadItem?.DownloadClientInfo?.Name,
                DownloadClientHasPostImportCategory = td.DownloadItem?.DownloadClientInfo?.HasPostImportCategory ?? false,
                Added = td.Added
            };

            // Deterministic Id per Q-3 RESEARCH lock — same TrackedDownload + chapter combo
            // yields same Id across refreshes (SignalR diff key).
            item.Id = HashConverter.GetHashInt31($"trackedDownload-{item.DownloadClient}-{item.DownloadId}-{chapter?.Id ?? 0}");

            if (item.TimeLeft.HasValue)
            {
                item.EstimatedCompletionTime = DateTime.UtcNow.Add(item.TimeLeft.Value);
            }

            return item;
        }
    }
}
