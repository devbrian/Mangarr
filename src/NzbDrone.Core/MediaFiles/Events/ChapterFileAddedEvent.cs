using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: EpisodeFileAddedEvent. Phase 8 cleanup: collapse on Tv/ deletion.
    // Published by ChapterFileService.Add after row insert; consumed by Plan 06-09 SignalR
    // hub for OnChapterFileImported broadcast.
    public class ChapterFileAddedEvent : IEvent
    {
        public ChapterFile ChapterFile { get; private set; }

        public ChapterFileAddedEvent(ChapterFile chapterFile)
        {
            ChapterFile = chapterFile;
        }
    }
}
