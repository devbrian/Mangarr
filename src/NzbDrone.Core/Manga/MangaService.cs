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

        // Mirrors Tv/SeriesService.AllForTag (Tv/SeriesService.cs:195) verbatim.
        // Closes Phase 8 audit gap-04: Settings/Tags UI manga-using-tag count + tag delete flow.
        public List<Manga> AllForTag(int tagId)
        {
            return GetAllManga().Where(m => m.Tags.Contains(tagId))
                                .ToList();
        }

        public List<int> AllMangaIds()
        {
            return _mangaRepository.All().Select(m => m.Id).ToList();
        }

        // Phase 8 audit gap-11 (SeriesService-vs-MangaService.md): manga peers of TV's
        // SeriesService.AllSeriesTvdbIds() (Tv/SeriesService.cs:175). Exposes the
        // cross-source ID lists for ImportList "skip-already-added" filters and
        // ImportListExclusion bookkeeping (D-16). Manga diverges from TV's single
        // TvdbId column by carrying three optional cross-source IDs (MangaDexId/MalId/
        // AniListId), so we expose three peer methods. Repository layer (MangaRepository
        // .AllMangaDexIds/AllMalIds/AllAniListIds) already filters out null values.
        public List<Guid> AllMangaDexIds()
        {
            return _mangaRepository.AllMangaDexIds();
        }

        public List<int> AllMalIds()
        {
            return _mangaRepository.AllMalIds();
        }

        public List<int> AllAniListIds()
        {
            return _mangaRepository.AllAniListIds();
        }

        public Dictionary<int, string> GetAllMangaPaths()
        {
            return _mangaRepository.AllMangaPaths();
        }

        // BL-09 fix: refuse to publish update events for a no-op update (manga
        // does not exist). Dapper's UPDATE ... WHERE Id silently no-ops on missing
        // rows, then SignalR would broadcast a phantom event the UI then refetches
        // and 404s on. Find-first prevents the phantom.
        //
        // Phase 8 audit gap-09: this method is the USER-EDIT path (mirrors TV's
        // SeriesService.UpdateSeries at Tv/SeriesService.cs:230). It now publishes
        // MangaEditedEvent(new, old) carrying the pre-update snapshot so handlers
        // can diff (path change → move, etc.). The pre-update snapshot is the row
        // currently in the DB; we capture it via the same Find call that gates the
        // existence check (single read, no extra query).
        //
        // RefreshMangaService still publishes MangaUpdatedEvent directly for the
        // post-sync pulse — its UpdateManga(publishUpdatedEvent:false) call here
        // suppresses ALL events from this path so there is no double-publish.
        public Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true)
        {
            if (manga == null)
            {
                throw new ArgumentNullException(nameof(manga));
            }

            var stored = _mangaRepository.Find(manga.Id);

            if (stored == null)
            {
                throw new ModelNotFoundException(typeof(Manga), manga.Id);
            }

            var updated = _mangaRepository.Update(manga);

            if (publishUpdatedEvent)
            {
                _eventAggregator.PublishEvent(new MangaEditedEvent(updated, stored));
            }

            return updated;
        }

        public bool MangaPathExists(string folder)
        {
            return _mangaRepository.MangaPathExists(folder);
        }

        // Mirrors Tv/SeriesService.RemoveAddOptions (Tv/SeriesService.cs:270): clears
        // Manga.AddOptions and persists via SetFields so only the AddOptions column is
        // written without firing MangaUpdatedEvent. Closes Phase 8 audit gap-07.
        public void RemoveAddOptions(Manga manga)
        {
            manga.AddOptions = null;
            _mangaRepository.SetFields(manga, m => m.AddOptions);
        }
    }
}
