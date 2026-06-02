using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistoryRepository.cs.
    // Role-match analog (exact): src/NzbDrone.Core/History/Manga/ChapterHistoryRepository.cs
    //   (BasicRepository<T>, (IMainDatabase, IEventAggregator) ctor, Query(h => h.DownloadId == ...)).
    //
    // TWO-SURFACE NOTE: this repository wraps the lean MangaDownloadHistory table, registered
    // separately in TableMapping.cs (Mapper.Entity<MangaDownloadHistory>("MangaDownloadHistory")).
    // It is DISTINCT from ChapterHistoryRepository — do NOT reuse ChapterHistory.FindByDownloadId.
    //
    // Anti-Pattern A (event-driven write): rows are NEVER written from this repository directly —
    // MangaDownloadHistoryService.Handle owns every Insert. This repository only reads/queries.
    public class MangaDownloadHistoryRepository : BasicRepository<MangaDownloadHistory>, IMangaDownloadHistoryRepository
    {
        public MangaDownloadHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MangaDownloadHistory> FindByDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId);
        }

        public MangaDownloadHistory GetLatestGrab(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId)
                .Where(h => h.EventType == MangaDownloadHistoryEventType.DownloadGrabbed)
                .MaxBy(h => h.Date);
        }
    }
}
