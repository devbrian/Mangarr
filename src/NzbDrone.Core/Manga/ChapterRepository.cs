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
