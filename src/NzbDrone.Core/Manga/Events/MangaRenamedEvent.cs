using System.Collections.Generic;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by RenameChapterFileService after a successful per-manga
    // rename pass. Mirrors Sonarr's SeriesRenamedEvent (MediaFiles/Events/SeriesRenamedEvent.cs)
    // verbatim shape — carries the manga plus the list of renamed chapter files so
    // subscribers (notifications, search recheck, history) can react.
    //
    // Publish call sites are deferred to a later RenameChapterFileService backfill;
    // this iteration ships the event class only.
    public class MangaRenamedEvent : IEvent
    {
        public Manga Manga { get; private set; }
        public List<RenamedChapterFile> RenamedFiles { get; private set; }

        public MangaRenamedEvent(Manga manga, List<RenamedChapterFile> renamedFiles)
        {
            Manga = manga;
            RenamedFiles = renamedFiles;
        }
    }
}
