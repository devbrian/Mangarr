using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Service implementation for Chapter row. Mirrors Sonarr's EpisodeService
    // (Tv/EpisodeService.cs:44-329) shape, slimmed for Phase 2 deliverables:
    //   * No IHandle<EpisodeFileDeletedEvent>/EpisodeFileAddedEvent — Phase 4 territory
    //   * No IHandleAsync<SeriesScannedEvent> — no scan in Phase 2
    //   * No IConfigService dependency (no AutoUnmonitor*Episodes config in Phase 2)
    //   * No ICached<HashSet<int>> — that supports the SeriesScanned tombstone cache
    //     which we don't need until Phase 4
    public class ChapterService : IChapterService
    {
        private readonly IChapterRepository _chapterRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public ChapterService(IChapterRepository chapterRepository,
                              IEventAggregator eventAggregator,
                              Logger logger)
        {
            _chapterRepository = chapterRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public Chapter GetChapter(int id)
        {
            return _chapterRepository.Get(id);
        }

        public List<Chapter> GetChapters(IEnumerable<int> ids)
        {
            return _chapterRepository.Get(ids).ToList();
        }

        public Chapter FindByMangaAndNumber(int mangaId, decimal chapterNumber, string translatedLanguage)
        {
            return _chapterRepository.Find(mangaId, chapterNumber, translatedLanguage);
        }

        public List<Chapter> GetChaptersByManga(int mangaId)
        {
            return _chapterRepository.GetByMangaId(mangaId);
        }

        public List<Chapter> GetSyntheticChaptersByManga(int mangaId)
        {
            return _chapterRepository.GetSyntheticByMangaId(mangaId);
        }

        public List<Chapter> AllMissingMonitoredChapters()
        {
            // Phase 6 D-09 — pass-through to repository. Consumed by Plan 06-06
            // MissingChapterSearchService which then filters by Manga.Monitored.
            return _chapterRepository.AllMissingMonitoredChapters();
        }

        public PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec)
        {
            // Plan 06-09 — pass-through. The repository pre-pends a `ChapterFileId IS NULL`
            // filter onto the spec; the controller composes monitored / mangaIds / languages
            // filters on top via PagingSpec.FilterExpressions.
            return _chapterRepository.ChaptersWithoutFiles(pagingSpec);
        }

        public void UpdateChapter(Chapter chapter)
        {
            _chapterRepository.Update(chapter);
        }

        public void SetChapterMonitored(int chapterId, bool monitored)
        {
            var chapter = _chapterRepository.Get(chapterId);
            chapter.Monitored = monitored;
            _chapterRepository.Update(chapter);

            _logger.Debug("Monitored flag for Chapter:{0} was set to {1}", chapterId, monitored);
        }

        public void InsertMany(List<Chapter> chapters)
        {
            _chapterRepository.InsertMany(chapters);
        }

        public void UpdateMany(List<Chapter> chapters)
        {
            _chapterRepository.UpdateMany(chapters);
        }

        public void DeleteMany(List<Chapter> chapters)
        {
            _chapterRepository.DeleteMany(chapters);
        }
    }
}
