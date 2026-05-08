using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by ChapterListService.SyncChapters (Plan 02-09) after the
    // chapter list for a Manga changes — synthesis ran, MangaDex linked, indexer
    // updated rows. Consumers re-read from IChapterRepository — the event does NOT
    // carry the delta. Mirrors Mangarr's SeriesUpdatedEvent single-arg shape.
    public class ChapterListUpdatedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public ChapterListUpdatedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
