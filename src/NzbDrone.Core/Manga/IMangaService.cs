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
        Manga FindByTitle(string title, int year);
        List<Manga> FindByTitleInexact(string title);
        Manga FindByPath(string path);
        void DeleteManga(List<int> mangaIds, bool deleteFiles);
        List<Manga> GetAllManga();
        List<Manga> AllForTag(int tagId);
        List<int> AllMangaIds();
        List<Guid> AllMangaDexIds();
        List<int> AllMalIds();
        List<int> AllAniListIds();
        Dictionary<int, string> GetAllMangaPaths();
        Dictionary<int, List<int>> GetAllMangaTags();
        Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true);
        List<Manga> UpdateManga(List<Manga> manga, bool useExistingRelativeFolder);
        bool MangaPathExists(string folder);
        void RemoveAddOptions(Manga manga);
    }
}
