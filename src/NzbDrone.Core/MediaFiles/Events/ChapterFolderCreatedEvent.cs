using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap (no-sibling/EpisodeFolderCreatedEvent).
    // Role-match analog: EpisodeFolderCreatedEvent. Phase 8 cleanup: collapse on Tv/ deletion.
    // Published per folder by ChapterFileMovingService after EnsureFolder succeeds (publish wiring
    // deferred to Plan 02-17). Consumers: Extras-style pipelines (cover.jpg writer, ComicInfo writer
    // — Komga/Kavita read cover.jpg from per-chapter folders).
    //
    // Shape note: TV's EpisodeFolderCreatedEvent carries Series + EpisodeFile + SeriesFolder +
    // SeasonFolder + EpisodeFolder. Manga sibling carries only ChapterFile + MangaFolder +
    // ChapterFolder, matching the existing manga file-event convention (ChapterFileAddedEvent /
    // ChapterFileDeletedEvent / ChapterFileRenamedEvent) — ChapterFile already exposes MangaId,
    // and the aggregate is reachable via IMangaService when needed. SeasonFolder is dropped per
    // D-13 (manga has no season concept).
    public class ChapterFolderCreatedEvent : IEvent
    {
        public ChapterFile ChapterFile { get; private set; }
        public string MangaFolder { get; set; }
        public string ChapterFolder { get; set; }

        public ChapterFolderCreatedEvent(ChapterFile chapterFile)
        {
            ChapterFile = chapterFile;
        }
    }
}
