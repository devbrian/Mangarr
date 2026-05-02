using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Dapper repository for Manga. Mirrors Sonarr's SeriesRepository
    // (Tv/SeriesRepository.cs:25-138) shape verbatim. Inherits Phase 1 D-15 Polly
    // retry coverage automatically via BasicRepository<T>.
    public class MangaRepository : BasicRepository<Manga>, IMangaRepository
    {
        public MangaRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public bool MangaPathExists(string path)
        {
            return Query(m => m.Path == path).Any();
        }

        public Manga FindByTitle(string cleanTitle)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            return Query(m => m.CleanTitle == cleanTitle).SingleOrDefault();
        }

        public Manga FindByMangaDexId(Guid mangaDexId)
        {
            return Query(m => m.MangaDexId == mangaDexId).SingleOrDefault();
        }

        public Manga FindByMalId(int malId)
        {
            return Query(m => m.MalId == malId).SingleOrDefault();
        }

        public Manga FindByAniListId(int aniListId)
        {
            return Query(m => m.AniListId == aniListId).SingleOrDefault();
        }

        public Manga FindByPath(string path)
        {
            return Query(m => m.Path == path).FirstOrDefault();
        }

        public List<Guid> AllMangaDexIds()
        {
            return All()
                .Where(m => m.MangaDexId.HasValue)
                .Select(m => m.MangaDexId.Value)
                .ToList();
        }

        public List<int> AllMalIds()
        {
            return All()
                .Where(m => m.MalId.HasValue)
                .Select(m => m.MalId.Value)
                .ToList();
        }

        public List<int> AllAniListIds()
        {
            return All()
                .Where(m => m.AniListId.HasValue)
                .Select(m => m.AniListId.Value)
                .ToList();
        }

        public Dictionary<int, string> AllMangaPaths()
        {
            return All().ToDictionary(m => m.Id, m => m.Path);
        }
    }
}
