namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 / HISTORY-01 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/EpisodeHistory.cs (EpisodeHistoryEventType enum).
    //
    // Phase 6 D-21 EventType enum. EXACTLY 5 values per HISTORY-01 wording.
    // Drops TV-specific EpisodeFileDeleted/EpisodeFileRenamed/SeriesFolderImported (manga has no
    // separate "renamed" or "season-folder-imported" lifecycle — chapter files are atomic CBZ
    // artifacts). Phase 8 cleanup: collapse with EpisodeHistoryEventType when Tv/ deletes.
    public enum ChapterHistoryEventType
    {
        Unknown = 0,
        Grabbed = 1,
        DownloadFailed = 2,
        Imported = 3,
        ImportFailed = 4,
        Ignored = 5
    }
}
