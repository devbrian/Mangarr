using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap (no-sibling/EpisodeFileRenamedEvent).
    // Role-match analog: EpisodeFileRenamedEvent. Phase 8 cleanup: collapse on Tv/ deletion.
    // Published per file by RenameChapterFileService after move-to-new-name (publish wiring
    // deferred to Plan 02-17). Consumers: history-writer + SignalR pushers (deferred).
    //
    // Shape note: TV's EpisodeFileRenamedEvent carries Series + EpisodeFile + OriginalPath.
    // Manga sibling carries only ChapterFile + OriginalPath, matching the existing manga
    // file-event convention (ChapterFileAddedEvent / ChapterFileDeletedEvent) — ChapterFile
    // already exposes MangaId, and the aggregate is reachable via IMangaService when needed.
    public class ChapterFileRenamedEvent : IEvent
    {
        public ChapterFile ChapterFile { get; private set; }
        public string OriginalPath { get; private set; }

        public ChapterFileRenamedEvent(ChapterFile chapterFile, string originalPath)
        {
            ChapterFile = chapterFile;
            OriginalPath = originalPath;
        }
    }
}
