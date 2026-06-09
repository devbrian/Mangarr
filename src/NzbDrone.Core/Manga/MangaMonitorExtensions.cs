namespace NzbDrone.Core.Manga
{
    // #356 D-1/D-4 — shared coarse new-chapter derivation. MonitorNewItems (the Sonarr
    // "monitor new seasons" peer) is no longer a user-facing axis; it is derived from the
    // Monitor choice at add/import time: None -> None, every other value -> All.
    //
    // Both the import-list path (ImportListSyncService) and the manga add path
    // (AddMangaService) consume this single helper, replacing the per-class private copy
    // that ImportListSyncService used to carry (DeriveMonitorNewItems(MonitorTypes)).
    public static class MangaMonitorExtensions
    {
        public static MangaMonitorNewItems DeriveMonitorNewItems(this MangaMonitor monitor) =>
            monitor == MangaMonitor.None
                ? MangaMonitorNewItems.None
                : MangaMonitorNewItems.All;
    }
}
