using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MangaService.DeleteManga after Delete. Mirrors Mangarr's
    // SeriesDeletedEvent (Tv/Events/SeriesDeletedEvent.cs) shape, slimmed: Phase 2
    // does not yet have ImportListExclusion (META-03 territory in Phase 2 surfaces
    // only as a developer relink endpoint — no exclusion-list bookkeeping needed
    // until Phase 7 UI ships ImportLists in v2).
    public class MangaDeletedEvent : IEvent
    {
        public Manga Manga { get; private set; }
        public bool DeleteFiles { get; private set; }

        public MangaDeletedEvent(Manga manga, bool deleteFiles)
        {
            Manga = manga;
            DeleteFiles = deleteFiles;
        }
    }
}
