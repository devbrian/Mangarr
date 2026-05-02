using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Service contract for Chapter row. Mirrors Sonarr's IEpisodeService
    // (Tv/EpisodeService.cs:15-42) shape, with manga-domain divergence:
    //   * FindEpisode(seriesId, season, episodeNumber) → FindByMangaAndNumber(
    //     mangaId, decimal chapterNumber, string translatedLanguage) — D-03 contract
    //     consumed by MangaParsingService.Map (Plan 02-04)
    //   * Drop EpisodeFile-related methods (Phase 4 archive-layer territory)
    //   * Drop Paging / EpisodeBetweenDates (Phase 7 UI territory)
    public interface IChapterService
    {
        Chapter GetChapter(int id);
        List<Chapter> GetChapters(IEnumerable<int> ids);
        Chapter FindByMangaAndNumber(int mangaId, decimal chapterNumber, string translatedLanguage);
        List<Chapter> GetChaptersByManga(int mangaId);
        List<Chapter> GetSyntheticChaptersByManga(int mangaId);
        void UpdateChapter(Chapter chapter);
        void SetChapterMonitored(int chapterId, bool monitored);
        void InsertMany(List<Chapter> chapters);
        void UpdateMany(List<Chapter> chapters);
        void DeleteMany(List<Chapter> chapters);
    }
}
