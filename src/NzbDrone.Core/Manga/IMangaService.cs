using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Service contract for Manga aggregate. Mirrors Sonarr's ISeriesService
    // (Tv/SeriesService.cs:12-37) shape, with manga-domain divergence:
    //   * FindByTvdbId etc. → FindByMangaDexId / FindByMalId / FindByAniListId
    //   * No IBuildSeriesPaths injection (manga path-build is bespoke; AddMangaService
    //     plan 02-09 owns it)
    //   * No IAutoTaggingService injection (auto-tagging is Phase 5+ territory)
    //   * Publishes MangaAddedEvent / MangaUpdatedEvent / MangaDeletedEvent (not
    //     SeriesEditedEvent — Phase 2 keeps the event model simple)
    public interface IMangaService
    {
        Manga GetManga(int mangaId);
        List<Manga> GetManga(IEnumerable<int> mangaIds);
        Manga AddManga(Manga newManga);
        List<Manga> AddManga(List<Manga> newManga);
        Manga FindByMangaDexId(Guid mangaDexId);
        Manga FindByMalId(int malId);
        Manga FindByAniListId(int aniListId);
        Manga FindByTitle(string title);
        Manga FindByPath(string path);
        void DeleteManga(List<int> mangaIds, bool deleteFiles);
        List<Manga> GetAllManga();
        List<int> AllMangaIds();
        Dictionary<int, string> GetAllMangaPaths();
        Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true);
        bool MangaPathExists(string folder);
    }
}
