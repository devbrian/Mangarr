using NzbDrone.Common.Messaging;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Download.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02 / LOOP-03) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/DownloadCompletedEvent.cs.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/MangaImport/ChapterImportedEvent.cs +
    //   src/NzbDrone.Core/MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs (IEvent, getter props).
    //
    // PUBLISHED LATER (Plan 03): MangaCompletedDownloadService.Import raises this AFTER the import
    // pipeline completes — Pitfall 4 ordering (never before IImportApprovedChapters.Import returns,
    // or the Komga/Kavita rescan handlers race a half-written library).
    // CONSUMED HERE (Plan 01): MangaDownloadHistoryService writes the DownloadImported join row on it.
    //
    // Carries the TrackedDownload so the history writer can re-source MangaId / ChapterIds from the
    // in-memory RemoteChapter projection (no JSON round-trip), plus the opaque DownloadId for the join key.
    public class ChapterDownloadCompletedEvent : IEvent
    {
        public TrackedDownload TrackedDownload { get; }
        public string DownloadId { get; }

        public ChapterDownloadCompletedEvent(TrackedDownload trackedDownload, string downloadId)
        {
            TrackedDownload = trackedDownload;
            DownloadId = downloadId;
        }
    }
}
