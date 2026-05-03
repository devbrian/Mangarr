using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 — published on terminal chapter-download failure. Phase 6 HISTORY-01
    /// subscribes to write a <c>DownloadFailed</c> History row.
    /// </summary>
    public sealed class ChapterDownloadFailedEvent : IEvent
    {
        public int RowId { get; }            // ChapterDownloadState.Id
        public int MangaId { get; }
        public int ChapterId { get; }
        public string FailureReason { get; }

        public ChapterDownloadFailedEvent(int rowId, int mangaId, int chapterId, string failureReason)
        {
            RowId = rowId;
            MangaId = mangaId;
            ChapterId = chapterId;
            FailureReason = failureReason;
        }
    }
}
