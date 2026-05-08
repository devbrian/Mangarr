using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Manga.Events
{
    // POCO event published by MoveMangaService after a successful library-folder
    // relocation. Mirrors Mangarr's SeriesMovedEvent (Tv/Events/SeriesMovedEvent.cs)
    // verbatim shape — carries the manga plus source/destination paths so subscribers
    // (notification on path change, search recheck) can react.
    //
    // Publish call sites are deferred to Plan 02-16 (MoveMangaService backfill);
    // this iteration ships the event class only.
    public class MangaMovedEvent : IEvent
    {
        public Manga Manga { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }

        public MangaMovedEvent(Manga manga, string sourcePath, string destinationPath)
        {
            Manga = manga;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
    }
}
