using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.MediaFiles.Events
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap (no-sibling/SeriesScanSkippedEvent).
    // Role-match analog: SeriesScanSkippedEvent. Phase 8 cleanup: collapse on Tv/ deletion.
    // Publish wiring deferred — manga DiskScanService backfill is a separate plan.
    public class MangaScanSkippedEvent : IEvent
    {
        public Manga.Manga Manga { get; private set; }
        public MangaScanSkippedReason Reason { get; set; }

        public MangaScanSkippedEvent(Manga.Manga manga, MangaScanSkippedReason reason)
        {
            Manga = manga;
            Reason = reason;
        }
    }

    public enum MangaScanSkippedReason
    {
        RootFolderDoesNotExist,
        RootFolderIsEmpty,
        NeverRescanAfterRefresh,
        RescanAfterManualRefreshOnly
    }
}
