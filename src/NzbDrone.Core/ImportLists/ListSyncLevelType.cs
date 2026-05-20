namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ListSyncLevelType.cs.
    //
    // Drives ImportListSyncService.TryCleanLibrary's per-config response when a manga
    // disappears from every enabled import list:
    //   Disabled         — never touch the library
    //   LogOnly          — emit a log line; library unchanged
    //   KeepAndUnmonitor — flip Manga.Monitored = false; keep the row
    //   KeepAndTag       — add Config.ListSyncTag to Manga.Tags; keep the row
    public enum ListSyncLevelType
    {
        Disabled,
        LogOnly,
        KeepAndUnmonitor,
        KeepAndTag
    }
}
