using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event mirroring Sonarr's SeriesBulkEditedEvent (Tv/Events/SeriesBulkEditedEvent.cs)
    // verbatim shape. Carries List<Manga> of bulk-edited entities. Published by
    // MangaService.UpdateManga(List<Manga>, bool). Sole subscriber is MangaController's
    // IHandle<MangaBulkEditedEvent> SignalR fan-out — mirroring Sonarr, the bulk path queues
    // NO refresh/rename (the root-folder move is owned by BulkMoveMangaCommand). See issue #264.
    public class MangaBulkEditedEvent : IEvent
    {
        public List<Manga> Manga { get; private set; }

        public MangaBulkEditedEvent(List<Manga> manga)
        {
            Manga = manga;
        }
    }
}
