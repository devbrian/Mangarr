using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.MangaImport;

namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/HistoryService.cs (IHistoryService).
    //
    // BL-01 GUARD: FindByChapterId is the manga-aware replacement for the Phase 5 STUB
    // that called the TV-side IHistoryService.FindByEpisodeId. Queries ChapterHistory.ChapterId
    // (manga sibling table) — independent of EpisodeHistory.EpisodeId. See
    // AlreadyImportedChapterSpecification for the consumer site.
    //
    // Phase 8 cleanup: collapse with IHistoryService when Tv/ deletes.
    public interface IChapterHistoryService
    {
        PagingSpec<ChapterHistory> Paged(PagingSpec<ChapterHistory> pagingSpec, int[] languages);
        ChapterHistory MostRecentForChapter(int chapterId);
        List<ChapterHistory> FindByChapterId(int chapterId);
        ChapterHistory MostRecentForDownloadId(string downloadId);
        ChapterHistory Get(int historyId);
        List<ChapterHistory> GetByManga(int mangaId, ChapterHistoryEventType? eventType);
        List<ChapterHistory> Find(string downloadId, ChapterHistoryEventType eventType);
        List<ChapterHistory> FindByDownloadId(string downloadId);
        string FindDownloadId(ChapterImportedEvent imported);
        List<ChapterHistory> Since(DateTime date, ChapterHistoryEventType? eventType);
    }
}
