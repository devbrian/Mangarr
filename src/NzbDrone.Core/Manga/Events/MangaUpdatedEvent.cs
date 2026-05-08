using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MangaService.UpdateManga after Update. Mirrors Mangarr's
    // SeriesUpdatedEvent (Tv/Events/SeriesUpdatedEvent.cs) verbatim shape.
    public class MangaUpdatedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public MangaUpdatedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
