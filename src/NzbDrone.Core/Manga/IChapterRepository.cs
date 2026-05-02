using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Repository contract for Chapter row. Mirrors Sonarr's IEpisodeRepository
    // (Tv/EpisodeRepository.cs:13-34) shape, with manga-domain divergence:
    //   * Find takes (mangaId, decimal chapterNumber, string translatedLanguage) —
    //     the Phase 1 composite index key (D-09 + 02-CONTEXT D-12 widen).
    //   * GetByMangaId mirrors GetEpisodes(int seriesId).
    //   * GetSyntheticByMangaId surfaces Chapter.IsSynthetic rows for the Phase 3
    //     indexer fill-in pipeline (D-17).
    //   * Drop SetFileId/ClearFileId — Phase 4 archive-layer territory.
    public interface IChapterRepository : IBasicRepository<Chapter>
    {
        Chapter Find(int mangaId, decimal chapterNumber, string translatedLanguage);
        List<Chapter> GetByMangaId(int mangaId);
        List<Chapter> GetSyntheticByMangaId(int mangaId);
        void SetMonitored(IEnumerable<int> ids, bool monitored);
    }
}
