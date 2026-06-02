using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistoryService.cs.
    // Role-match analog (exact event-driven write shape): src/NzbDrone.Core/History/Manga/ChapterHistoryService.cs
    //   (IHandle<ChapterGrabbedEvent>; rows written from the SERVICE Handle, never the repository).
    //
    // TWO-SURFACE NOTE: this service writes the LEAN DownloadId-keyed matching join
    // (MangaDownloadHistory), DISTINCT from the user-facing ChapterHistory. The LOOP-02 matcher
    // calls GetLatestGrab(downloadId) on this surface. Do NOT reuse ChapterHistory.FindByDownloadId.
    //
    // Anti-Pattern A guard: every Insert lives in a Handle method below — the repository never writes.
    //
    // Insert-FIRST (Pitfall): on ChapterGrabbedEvent the Grabbed join row is inserted synchronously
    // inside Handle BEFORE control returns, so a poll cycle landing immediately after the grab
    // (TrackedDownloadService → GetLatestGrab) finds the row. Mirrors MangaBlocklistService
    // Insert-before-publish.
    public class MangaDownloadHistoryService : IMangaDownloadHistoryService,
                                               IHandle<ChapterGrabbedEvent>,
                                               IHandle<ChapterDownloadCompletedEvent>
    {
        private readonly IMangaDownloadHistoryRepository _repository;
        private readonly Logger _logger;

        public MangaDownloadHistoryService(IMangaDownloadHistoryRepository repository, Logger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public MangaDownloadHistory GetLatestGrab(string downloadId)
        {
            return _repository.GetLatestGrab(downloadId);
        }

        public void Handle(ChapterGrabbedEvent message)
        {
            // A grab with no DownloadId can never be matched back to a download client item, so it
            // contributes nothing to the join — skip it (defensive; the in-process client always
            // supplies a DownloadId).
            if (string.IsNullOrWhiteSpace(message.DownloadId))
            {
                return;
            }

            var remote = message.RemoteChapter;
            if (remote?.Manga == null)
            {
                return;
            }

            // Pitfall 2: resolve ALL chapter ids from the RemoteChapter — a multi-chapter pack
            // (c179/c180/c181) records ChapterIds = [179,180,181], never collapsing to one.
            var chapterIds = (remote.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>())
                .Select(c => c.Id)
                .ToList();

            var history = new MangaDownloadHistory
            {
                EventType = MangaDownloadHistoryEventType.DownloadGrabbed,
                Date = DateTime.UtcNow,
                DownloadId = message.DownloadId,
                MangaId = remote.Manga.Id,
                ChapterIds = chapterIds,
                SourceTitle = remote.Release?.Title
            };

            history.Data.Add("DownloadClient", message.DownloadClient ?? string.Empty);
            history.Data.Add("Indexer", remote.Release?.Indexer ?? string.Empty);

            // Insert-FIRST — synchronous before Handle returns (Pitfall: a poll right after grab
            // must find this row via GetLatestGrab).
            _repository.Insert(history);
        }

        public void Handle(ChapterDownloadCompletedEvent message)
        {
            if (string.IsNullOrWhiteSpace(message.DownloadId))
            {
                return;
            }

            var remote = message.TrackedDownload?.RemoteChapter;
            if (remote?.Manga == null)
            {
                return;
            }

            var chapterIds = (remote.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>())
                .Select(c => c.Id)
                .ToList();

            var history = new MangaDownloadHistory
            {
                EventType = MangaDownloadHistoryEventType.DownloadImported,
                Date = DateTime.UtcNow,
                DownloadId = message.DownloadId,
                MangaId = remote.Manga.Id,
                ChapterIds = chapterIds,
                SourceTitle = remote.Release?.Title
            };

            _repository.Insert(history);
        }
    }
}
