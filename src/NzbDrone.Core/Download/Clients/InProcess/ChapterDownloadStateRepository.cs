using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 D-05/D-07 — Repository for the hybrid resumable-state row.
    /// Inherits <see cref="BasicRepository{TModel}"/> Polly resilience (Phase 1 D-15) — DO NOT
    /// add a custom retry pipeline; the per-page progress UPDATE storm benefits from the
    /// shared SQLite busy_timeout=5000 + Polly floor.
    /// </summary>
    public class ChapterDownloadStateRepository : BasicRepository<ChapterDownloadState>, IChapterDownloadStateRepository
    {
        public ChapterDownloadStateRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public IEnumerable<ChapterDownloadState> AllInFlight()
        {
            // Phase 1 D-15 — capture UtcNow into a local so the LINQ expression visitor
            // emits a SQL parameter rather than re-evaluating a property access per row.
            var now = DateTime.UtcNow;
            return Query(c =>
                   c.Status == ChapterDownloadStatus.Queued
                || c.Status == ChapterDownloadStatus.Downloading
                || c.Status == ChapterDownloadStatus.Completing
                || c.Status == ChapterDownloadStatus.Completed
                || (c.Status == ChapterDownloadStatus.Failed
                    && (c.RetentionUntil == null || c.RetentionUntil > now)));
        }

        public IEnumerable<ChapterDownloadState> ByStatus(ChapterDownloadStatus status)
        {
            return Query(c => c.Status == status);
        }

        public ChapterDownloadState FindByMangaAndChapter(int mangaId, int chapterId)
        {
            return Query(c => c.MangaId == mangaId && c.ChapterId == chapterId).SingleOrDefault();
        }

        public void DeleteOrphans(DateTime olderThanRetentionUntil)
        {
            Delete(c => c.RetentionUntil != null && c.RetentionUntil < olderThanRetentionUntil);
        }
    }
}
