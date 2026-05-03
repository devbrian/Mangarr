using NzbDrone.Common.Messaging;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving
{
    /// <summary>
    /// Phase 4 — published on terminal chapter-download failure. Phase 6 HISTORY-01
    /// subscribes to write a <c>DownloadFailed</c> History row.
    /// </summary>
    /// <remarks>
    /// Phase 6 D-19/D-21 extension: un-sealed and gained optional <c>Source</c> /
    /// <c>DownloadClient</c> / <c>Release</c> / <c>SourceTitle</c> properties consumed
    /// by Plans 06-03 (history Data hydration) and 06-04 (blocklist auto-insert from
    /// release identity triple). Existing 4-arg ctor preserved so Phase 4 emit sites
    /// continue to compile; new fields use object-initializer syntax.
    /// </remarks>
    public class ChapterDownloadFailedEvent : IEvent
    {
        public int RowId { get; }            // ChapterDownloadState.Id
        public int MangaId { get; }
        public int ChapterId { get; }
        public string FailureReason { get; }

        // Phase 6 extensions — populated by call sites that have the data in scope; null otherwise.
        public string SourceTitle { get; init; }
        public string Source { get; init; }
        public string DownloadClient { get; init; }
        public ReleaseInfo Release { get; init; }

        // Alias for log-message text — Plan 06-04 reads .Reason; existing Phase 4 code reads .FailureReason.
        public string Message => FailureReason;
        public string Reason => FailureReason;

        public ChapterDownloadFailedEvent(int rowId, int mangaId, int chapterId, string failureReason)
        {
            RowId = rowId;
            MangaId = mangaId;
            ChapterId = chapterId;
            FailureReason = failureReason;
        }
    }
}
