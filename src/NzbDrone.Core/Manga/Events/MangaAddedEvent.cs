using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MangaService.AddManga after Insert. Mirrors Mangarr's
    // SeriesAddedEvent (Tv/Events/SeriesAddedEvent.cs) verbatim shape.
    public class MangaAddedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public MangaAddedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
