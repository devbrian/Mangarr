using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event mirroring Mangarr's SeriesAddCompletedEvent (Tv/Events/SeriesAddCompletedEvent.cs)
    // verbatim shape. Single-property IEvent carrying the just-added Manga; intended to be
    // published after the post-add monitoring + initial-search dispatch (publish wiring deferred
    // to AddMangaService bulk-add cleanup — see Phase 8 Plan 03-04+).
    public class MangaAddCompletedEvent : IEvent
    {
        public Manga Manga { get; private set; }

        public MangaAddCompletedEvent(Manga manga)
        {
            Manga = manga;
        }
    }
}
