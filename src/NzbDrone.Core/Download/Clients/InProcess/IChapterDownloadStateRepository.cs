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

        // Phase 6 PIPELINE-04 — delete state row after import success (Plans 06-07/08 lifecycle hook).
        // Silent no-op when no row matches (idempotent).
        void DeleteByChapterId(int chapterId);

        // Phase 9 Plan 09-14 (sub-wave A 09-05 audit gap-06 close-out): downloadId back-resolve
        // for ManualImportService.GetMediaFiles fast-path. Mirrors TV ITrackedDownloadService.Find
        // shape semantically — returns null when downloadId is stale / not in-flight (caller falls
        // back to the existing folder-fallback chain). Used by:
        //   src/NzbDrone.Core/MediaFiles/MangaImport/Manual/ManualImportService.cs
        //
        // Schema-substitution note (audit gap-06 OPTION A adaptation): the audit's snippet assumed
        // a `DownloadClientId : string` column on ChapterDownloadState. Actual schema (Phase 4 D-05)
        // surfaces the row's identity to download-client consumers as `Id.ToString("D")` — see
        // InProcessImageDownloadClient.GetItems() line ~83 (DownloadId = row.Id.ToString("D")).
        // This finder accepts that string form, parses it back to an int, and delegates to
        // BasicRepository.Find(int) (silent null on miss / non-numeric input).
        ChapterDownloadState FindByDownloadId(string downloadId);
    }
}
