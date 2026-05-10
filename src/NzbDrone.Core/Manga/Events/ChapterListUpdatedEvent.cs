using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RefreshMangaService after the canonical Chapter list for
    // a Manga changes — IChapterListService.SyncChapters (Phase 16.1 single-pass;
    // mirrors Sonarr's IRefreshEpisodeService.RefreshEpisodeInfo) ran, MangaDex linked,
    // indexer updated rows. Consumers re-read from IChapterRepository — the event does
    // NOT carry the delta. Mirrors Mangarr's SeriesUpdatedEvent single-arg shape.
    // Pitfall 4: SINGLE emit per refresh from RefreshMangaService AFTER SyncChapters
    // completes; never emitted from within ChapterListService.
    public class ChapterListUpdatedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public ChapterListUpdatedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
