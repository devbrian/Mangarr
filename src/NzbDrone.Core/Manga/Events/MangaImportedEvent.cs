using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // Phase 8 audit gap-10 (SeriesService-vs-MangaService.md): bulk-add fan-out event
    // mirroring Tv/Events/SeriesImportedEvent.cs verbatim shape. Published once by
    // MangaService.AddManga(List<Manga>) after the bulk Insert with all newly-added
    // ids. MangaAddedHandler.Handle(MangaImportedEvent) batches the resulting
    // RefreshMangaCommands into a single PushMany — replaces the prior N-event-per-add
    // loop that fired N MangaAddedEvent → N RefreshMangaCommand.Push calls (rate-limit
    // budget pressure on ImportList ingestion / bulk-add-from-list flows).
    public class MangaImportedEvent : IEvent
    {
        public List<int> MangaIds { get; private set; }

        public MangaImportedEvent(List<int> mangaIds)
        {
            MangaIds = mangaIds;
        }
    }
}
