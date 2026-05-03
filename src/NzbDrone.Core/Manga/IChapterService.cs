using System.Collections.Generic;
using NzbDrone.Core.Datastore;

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

        // Phase 6 D-09 — Missing/Wanted feed: monitored Chapters that have no ChapterFile imported.
        // Manga.Monitored filtering is applied at the consumer layer (Plan 06-06
        // MissingChapterSearchService) — this method returns all monitored chapter rows with
        // ChapterFileId IS NULL regardless of parent manga state.
        List<Chapter> AllMissingMonitoredChapters();

        // Plan 06-09 (Rule 3) — paged variant for the V5 Wanted/Missing controller. Sibling of
        // TV's `IEpisodeService.EpisodesWithoutFiles(PagingSpec, bool includeSpecials)`. The
        // monitored filter is APPLIED INSIDE the paging spec by the controller (so callers can
        // opt out via `monitored=false` query). The repository walks `Chapter.ChapterFileId IS NULL`
        // and applies the spec's FilterExpressions on top.
        PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec);

        void UpdateChapter(Chapter chapter);
        void SetChapterMonitored(int chapterId, bool monitored);
        void InsertMany(List<Chapter> chapters);
        void UpdateMany(List<Chapter> chapters);
        void DeleteMany(List<Chapter> chapters);
    }
}
