namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistoryEventType.cs
    //   (re-skinned: the upstream DownloadFolderImported/FileImported flavors collapse to the
    //   manga-shape values below).
    // Role-match analog: src/NzbDrone.Core/History/Manga/ChapterHistoryEventType.cs.
    //
    // DISTINCT from the user-facing ChapterHistoryEventType — this enum belongs to the lean
    // DownloadId-keyed matching join (MangaDownloadHistory), the second of Sonarr's deliberate
    // two history surfaces. Do NOT reuse ChapterHistoryEventType here.
    public enum MangaDownloadHistoryEventType
    {
        DownloadGrabbed = 1,
        DownloadImported = 2,
        DownloadFailed = 3,
        DownloadIgnored = 4,
        FileImported = 5
    }
}
