using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Service implementation for Manga aggregate. Mirrors Sonarr's SeriesService
    // (Tv/SeriesService.cs:39-313) event-publish-after-insert pattern verbatim, slimmed
    // for Phase 2 deliverables (no IBuildSeriesPaths, no IAutoTaggingService — those
    // are Phase 5+ territory per 02-CONTEXT Out-of-Phase 2).
    public class MangaService : IMangaService
    {
        private readonly IMangaRepository _mangaRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaService(IMangaRepository mangaRepository,
                            IEventAggregator eventAggregator,
                            Logger logger)
        {
            _mangaRepository = mangaRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public Manga GetManga(int mangaId)
        {
            return _mangaRepository.Get(mangaId);
        }

        public List<Manga> GetManga(IEnumerable<int> mangaIds)
        {
            return _mangaRepository.Get(mangaIds).ToList();
        }

        public Manga AddManga(Manga newManga)
        {
            _mangaRepository.Insert(newManga);
            _eventAggregator.PublishEvent(new MangaAddedEvent(GetManga(newManga.Id)));

            return newManga;
        }

        public List<Manga> AddManga(List<Manga> newManga)
        {
            _mangaRepository.InsertMany(newManga);

            foreach (var manga in newManga)
            {
                _eventAggregator.PublishEvent(new MangaAddedEvent(GetManga(manga.Id)));
            }

            return newManga;
        }

        public Manga FindByMangaDexId(Guid mangaDexId)
        {
            return _mangaRepository.FindByMangaDexId(mangaDexId);
        }

        public Manga FindByMalId(int malId)
        {
            return _mangaRepository.FindByMalId(malId);
        }

        public Manga FindByAniListId(int aniListId)
        {
            return _mangaRepository.FindByAniListId(aniListId);
        }

        public Manga FindByTitle(string title)
        {
            return _mangaRepository.FindByTitle(title);
        }

        public Manga FindByPath(string path)
        {
            return _mangaRepository.FindByPath(path);
        }

        public void DeleteManga(List<int> mangaIds, bool deleteFiles)
        {
            var mangaList = _mangaRepository.Get(mangaIds).ToList();

            foreach (var manga in mangaList)
            {
                _mangaRepository.Delete(manga);
                _eventAggregator.PublishEvent(new MangaDeletedEvent(manga, deleteFiles));
            }
        }

        public List<Manga> GetAllManga()
        {
            return _mangaRepository.All().ToList();
        }

        public List<int> AllMangaIds()
        {
            return _mangaRepository.All().Select(m => m.Id).ToList();
        }

        public Dictionary<int, string> GetAllMangaPaths()
        {
            return _mangaRepository.AllMangaPaths();
        }

        public Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true)
        {
            var updated = _mangaRepository.Update(manga);

            if (publishUpdatedEvent)
            {
                _eventAggregator.PublishEvent(new MangaUpdatedEvent(updated));
            }

            return updated;
        }

        public bool MangaPathExists(string folder)
        {
            return _mangaRepository.MangaPathExists(folder);
        }
    }
}
