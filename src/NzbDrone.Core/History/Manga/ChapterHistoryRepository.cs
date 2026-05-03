using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/History/HistoryRepository.cs.
    //
    // BL-01 GUARD: FindByChapterId queries ChapterHistory.ChapterId (this table only) —
    // never reaches across to EpisodeHistory.EpisodeId. The two tables live in different
    // SQLite tables registered separately in TableMapping.cs — Dapper Query<ChapterHistory>
    // cannot accidentally hydrate from the History (TV) table.
    //
    // Phase 8 cleanup: collapse with HistoryRepository when Tv/ deletes.
    public class ChapterHistoryRepository : BasicRepository<ChapterHistory>, IChapterHistoryRepository
    {
        public ChapterHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public ChapterHistory MostRecentForChapter(int chapterId)
        {
            return Query(h => h.ChapterId == chapterId).MaxBy(h => h.Date);
        }

        public List<ChapterHistory> FindByChapterId(int chapterId)
        {
            return Query(h => h.ChapterId == chapterId)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        public ChapterHistory MostRecentForDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId).MaxBy(h => h.Date);
        }

        public List<ChapterHistory> FindByDownloadId(string downloadId)
        {
            return Query(h => h.DownloadId == downloadId);
        }

        public List<ChapterHistory> GetByManga(int mangaId, ChapterHistoryEventType? eventType)
        {
            var query = Query(h => h.MangaId == mangaId);

            if (eventType.HasValue)
            {
                query = query.Where(h => h.EventType == eventType.Value).ToList();
            }

            return query.OrderByDescending(h => h.Date).ToList();
        }

        public void DeleteForManga(int mangaId)
        {
            Delete(c => c.MangaId == mangaId);
        }

        public List<ChapterHistory> Since(DateTime date, ChapterHistoryEventType? eventType)
        {
            var query = Query(h => h.Date >= date);

            if (eventType.HasValue)
            {
                query = query.Where(h => h.EventType == eventType.Value).ToList();
            }

            return query.OrderBy(h => h.Date).ToList();
        }

        // Hides BasicRepository<TModel>.GetPaged(PagingSpec<TModel>) so the languages-filter
        // overload is exposed at the IChapterHistoryRepository contract level. V1 simplification:
        // no JOIN to Manga/Chapter tables (TV's HistoryRepository joins for Series + Episode
        // hydration to support the V5 controller's expanded payload). Plan 06-09 V5 controller
        // will hydrate Manga/Chapter at the controller layer via the existing services. Language
        // filter is accepted but is a no-op in V1 (the manga TranslatedLanguage column is BCP-47
        // string, not the Sonarr Language enum int[]) — wired so the V5 controller signature
        // matches the V3 history controller convention without creating a divergent surface.
        public PagingSpec<ChapterHistory> GetPaged(PagingSpec<ChapterHistory> pagingSpec, int[] languages)
        {
            return GetPaged(pagingSpec);
        }
    }
}
