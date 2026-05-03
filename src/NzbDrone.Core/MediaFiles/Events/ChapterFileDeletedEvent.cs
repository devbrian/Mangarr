using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: EpisodeFileDeletedEvent. Phase 8 cleanup: collapse on Tv/ deletion.
    public class ChapterFileDeletedEvent : IEvent
    {
        public ChapterFile ChapterFile { get; private set; }
        public DeleteMediaFileReason Reason { get; private set; }

        public ChapterFileDeletedEvent(ChapterFile chapterFile, DeleteMediaFileReason reason)
        {
            ChapterFile = chapterFile;
            Reason = reason;
        }
    }
}
