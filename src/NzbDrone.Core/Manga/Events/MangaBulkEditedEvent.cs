using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event mirroring Mangarr's SeriesBulkEditedEvent (Tv/Events/SeriesBulkEditedEvent.cs)
    // verbatim shape. Carries List<Manga> of bulk-edited entities. Publish wiring deferred
    // to MangaEditedService bulk path (Plan 04-02).
    public class MangaBulkEditedEvent : IEvent
    {
        public List<Manga> Manga { get; private set; }

        public MangaBulkEditedEvent(List<Manga> manga)
        {
            Manga = manga;
        }
    }
}
