using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RefreshMangaService (Phase 16 Plan 16-03) after the
    // canonical Chapter list for a Manga changes — EnsureChapter + SyncChapterReleases
    // ran, MangaDex linked, indexer updated rows. Consumers re-read from
    // IChapterRepository / IChapterReleaseRepository — the event does NOT carry the
    // delta. Mirrors Mangarr's SeriesUpdatedEvent single-arg shape. Pitfall 4: SINGLE
    // emit per refresh from RefreshMangaService AFTER both passes complete; never
    // emitted from within ChapterListService.
    public class ChapterListUpdatedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public ChapterListUpdatedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
