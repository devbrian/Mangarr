using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 D-05/D-07 — Repository contract for the in-flight chapter download state row.
    /// Mirrors Phase 3 <c>IIndexerSourceStatusRepository</c> shape (Q-2 Option A precedent).
    /// </summary>
    public interface IChapterDownloadStateRepository : IBasicRepository<ChapterDownloadState>
    {
        // GetItems() poll path (plan 04-03). Returns rows with Status NOT terminal-and-retention-expired.
        IEnumerable<ChapterDownloadState> AllInFlight();

        IEnumerable<ChapterDownloadState> ByStatus(ChapterDownloadStatus status);

        // App-level dedup check (RESEARCH.md Q-1 Option (b) — no DB unique index for partial-index portability).
        ChapterDownloadState FindByMangaAndChapter(int mangaId, int chapterId);

        // Housekeeper retention sweep (D-08; plan 04-08 invokes).
        void DeleteOrphans(DateTime olderThanRetentionUntil);
    }
}
