using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 → Phase 6 hand-off — published when an archiver completes successfully.
    /// Phase 6 PIPELINE-04 subscribes to dispatch ImportApprovedChapters; Phase 6 may also
    /// poll <c>ChapterDownloadState.Status=Completed</c> rows (RESEARCH.md Q-3 — both paths
    /// for resilience).
    /// </summary>
    public sealed class ChapterArchivedEvent : IEvent
    {
        public int MangaId { get; }
        public int ChapterId { get; }
        public string StagingPath { get; }
        public string OutputFormat { get; }   // "cbz" | "folder"

        public ChapterArchivedEvent(int mangaId, int chapterId, string stagingPath, string outputFormat)
        {
            MangaId = mangaId;
            ChapterId = chapterId;
            StagingPath = stagingPath;
            OutputFormat = outputFormat;
        }
    }
}
