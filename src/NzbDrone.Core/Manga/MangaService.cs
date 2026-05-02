using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
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

        // BL-01 fix: Find returns null on missing (Get throws ModelNotFoundException),
        // so the controller's `Results<Accepted, NotFound>` declared NotFound branch
        // is now reachable. Callers that need the "must exist" guarantee should call
        // _mangaRepository.Get directly or handle the null themselves.
        public Manga GetManga(int mangaId)
        {
            return _mangaRepository.Find(mangaId);
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

        // BL-09 fix: BasicRepository.Get(IEnumerable<int>) throws if ANY id is missing,
        // aborting the entire batch with a 500. Filter to the ids that actually exist
        // first so partial-batch deletes succeed and missing ids are silently skipped
        // (matches Sonarr's tolerant Delete shape — a 404-on-each is the controller's
        // job, not the service's).
        public void DeleteManga(List<int> mangaIds, bool deleteFiles)
        {
            var mangaList = _mangaRepository.All()
                .Where(m => mangaIds.Contains(m.Id))
                .ToList();

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

        // BL-09 fix: refuse to publish MangaUpdatedEvent for a no-op update (manga
        // does not exist). Dapper's UPDATE ... WHERE Id silently no-ops on missing
        // rows, then SignalR would broadcast a phantom Updated event the UI then
        // refetches and 404s on. Find-first prevents the phantom.
        public Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true)
        {
            if (manga == null)
            {
                throw new ArgumentNullException(nameof(manga));
            }

            if (_mangaRepository.Find(manga.Id) == null)
            {
                throw new ModelNotFoundException(typeof(Manga), manga.Id);
            }

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
