namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistoryService.cs
    //   (IDownloadHistoryService).
    // Role-match analog: src/NzbDrone.Core/History/Manga/IChapterHistoryService.cs.
    //
    // TWO-SURFACE NOTE: GetLatestGrab reads the lean MangaDownloadHistory matching join — the LOOP-02
    // matcher (Plan 02) calls it FIRST, falling back to title-parse only on a null. Do NOT reuse the
    // user-facing ChapterHistory for this lookup.
    public interface IMangaDownloadHistoryService
    {
        MangaDownloadHistory GetLatestGrab(string downloadId);
    }
}
