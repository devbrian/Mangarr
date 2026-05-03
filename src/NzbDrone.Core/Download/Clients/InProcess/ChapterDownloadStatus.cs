namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 D-05 — lifecycle states for an in-flight chapter download.
    /// Maps to <see cref="DownloadItemStatus"/> via <c>InProcessImageDownloadClient.MapStatus</c>
    /// (plan 04-03). Phase 8 cleanup: stays as-is (manga-shaped from Day 1).
    /// </summary>
    public enum ChapterDownloadStatus
    {
        Queued = 0,
        Downloading = 1,
        Completing = 2,   // pages all on disk; archiver in progress
        Completed = 3,
        Failed = 4
    }
}
