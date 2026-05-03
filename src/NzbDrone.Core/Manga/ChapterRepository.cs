using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Dapper repository for Chapter. Mirrors Sonarr's EpisodeRepository
    // (Tv/EpisodeRepository.cs:36-285) shape, slimmed to Phase 2 deliverables —
    // PagedQuery joins / EpisodesWithFiles / EpisodesWhereCutoffUnmet /
    // SetFileId / ClearFileId all stay in TV land (Phase 4 archiver territory).
    public class ChapterRepository : BasicRepository<Chapter>, IChapterRepository
    {
        private readonly Logger _logger;

        public ChapterRepository(IMainDatabase database, IEventAggregator eventAggregator, Logger logger)
            : base(database, eventAggregator)
        {
            _logger = logger;
        }

        public Chapter Find(int mangaId, decimal chapterNumber, string translatedLanguage)
        {
            return Query(c => c.MangaId == mangaId
                              && c.ChapterNumber == chapterNumber
                              && c.TranslatedLanguage == translatedLanguage)
                .SingleOrDefault();
        }

        public List<Chapter> GetByMangaId(int mangaId)
        {
            return Query(c => c.MangaId == mangaId).ToList();
        }

        public List<Chapter> GetSyntheticByMangaId(int mangaId)
        {
            return Query(c => c.MangaId == mangaId && c.IsSynthetic).ToList();
        }

        public List<Chapter> AllMissingMonitoredChapters()
        {
            // Monitored chapter rows with no ChapterFile imported yet.
            // Manga.Monitored filter applied at the consumer layer (Plan 06-06
            // MissingChapterSearchService) — this method intentionally returns chapters
            // for unmonitored manga so consumers can choose their own filter logic.
            return Query(c => c.Monitored && c.ChapterFileId == null).ToList();
        }

        public PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec)
        {
            // Plan 06-09 — paged variant for the V5 Wanted/Missing controller. Pre-pends a
            // "Chapter.ChapterFileId IS NULL" filter onto the spec; any caller-supplied
            // FilterExpressions (mangaIds, monitored, languages) compose on top via the
            // existing BasicRepository.AddFilters pipeline. V1 simplification: no JOIN to
            // Manga table — controller hydrates Manga via service-layer Get when needed
            // (mirrors Plan 06-03 ChapterHistoryRepository.GetPaged convention).
            pagingSpec.FilterExpressions.Add(c => c.ChapterFileId == null);
            return GetPaged(pagingSpec);
        }

        public void SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            var chapters = Get(ids).ToList();
            foreach (var chapter in chapters)
            {
                chapter.Monitored = monitored;
            }

            UpdateMany(chapters);
        }
    }
}
