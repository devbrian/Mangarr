using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/HistoryRepository.cs (IHistoryRepository).
    // Phase 8 cleanup: collapse with IHistoryRepository when Tv/ deletes.
    public interface IChapterHistoryRepository : IBasicRepository<ChapterHistory>
    {
        ChapterHistory MostRecentForChapter(int chapterId);
        List<ChapterHistory> FindByChapterId(int chapterId);
        ChapterHistory MostRecentForDownloadId(string downloadId);
        List<ChapterHistory> FindByDownloadId(string downloadId);
        List<ChapterHistory> GetByManga(int mangaId, ChapterHistoryEventType? eventType);
        void DeleteForManga(int mangaId);
        List<ChapterHistory> Since(DateTime date, ChapterHistoryEventType? eventType);
        PagingSpec<ChapterHistory> GetPaged(PagingSpec<ChapterHistory> pagingSpec, int[] languages);
    }
}
