using System.Collections.Generic;
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event mirroring Sonarr's SeriesScannedEvent (MediaFiles/Events/SeriesScannedEvent.cs)
    // verbatim shape. IEvent carrying the just-scanned Manga + PossibleExtraFiles list;
    // intended to be published by manga DiskScanService at end of scan (publish wiring deferred
    // to disk-scan / scanned-handler integration — see audit no-sibling/SeriesScannedEvent.md).
    public class MangaScannedEvent : IEvent
    {
        public Manga Manga { get; private set; }
        public List<string> PossibleExtraFiles { get; set; }

        public MangaScannedEvent(Manga manga, List<string> possibleExtraFiles)
        {
            Manga = manga;
            PossibleExtraFiles = possibleExtraFiles;
        }
    }
}
