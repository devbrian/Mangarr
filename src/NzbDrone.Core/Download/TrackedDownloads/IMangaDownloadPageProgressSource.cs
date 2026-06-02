namespace NzbDrone.Core.Download.TrackedDownloads
{
    // Sonarr divergence: NEW manga-only seam per Phase 36 (D-01 / D-01a / Q-D01) — see DIVERGENCE.md.
    // No Sonarr provenance — Sonarr has no page concept. This is the page-progress channel that lets
    // manga-native "page 7/20" reach the Queue projection WITHOUT bleeding Page* fields into the
    // shared DownloadClientItem POCO (which Phase 38's GatewayDownloadClient implements, bytes-only).
    //
    // An implementation reports page progress for the DownloadIds it owns and returns null for any
    // it does not (the in-process client supplies one keyed by its ChapterDownloadState rows; the
    // gateway path supplies NONE). MangaTrackedDownloadService aggregates the optional set and
    // exposes GetPageProgress(DownloadId); when every source returns null (gateway path) the Queue
    // caption falls back to bytes/% gracefully (D-01b).
    public interface IMangaDownloadPageProgressSource
    {
        // Returns the page counts for the given DownloadId, or null when this source does not own
        // that id. MUST be side-effect-free and tolerant of unknown ids.
        MangaDownloadPageProgress GetPageProgress(string downloadId);
    }

    // Manga-side page-progress carrier (D-01). Intentionally NOT part of DownloadClientItem — it is
    // an additive manga channel, absent on the gateway path (null lookup result).
    public class MangaDownloadPageProgress
    {
        public MangaDownloadPageProgress(int totalPages, int completedPages)
        {
            TotalPages = totalPages;
            CompletedPages = completedPages;
        }

        public int TotalPages { get; }
        public int CompletedPages { get; }
    }
}
