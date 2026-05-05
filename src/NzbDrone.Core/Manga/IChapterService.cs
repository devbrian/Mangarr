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

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-10) — bulk overload, sibling of TV's
        // `IEpisodeService.GetEpisodesBySeries(List<int> seriesIds)` (Tv/EpisodeService.cs:103-106).
        // Pass-through to `IChapterRepository.GetChaptersByMangaIds`. Used by ImportLists, bulk
        // operations, and multi-manga health-check / wanted-search flows.
        List<Chapter> GetChaptersByManga(List<int> mangaIds);

        List<Chapter> GetSyntheticChaptersByManga(int mangaId);

        // Phase 6 D-09 — Missing/Wanted feed: monitored Chapters that have no ChapterFile imported.
        // Manga.Monitored filtering is applied at the consumer layer (Plan 06-06
        // MissingChapterSearchService) — this method returns all monitored chapter rows with
        // ChapterFileId IS NULL regardless of parent manga state.
        List<Chapter> AllMissingMonitoredChapters();

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-11) — sibling of TV's
        // `IEpisodeService.EpisodesWithFiles(int seriesId)`. Inverse of the Missing feed: returns
        // chapters for a manga that DO have a ChapterFile imported. Used by Phase 6 file-rename
        // pipeline, housekeeping, and bulk operations.
        List<Chapter> ChaptersWithFiles(int mangaId);

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-02) — sibling of TV's
        // `IEpisodeService.GetEpisodesByFileId(int episodeFileId)` (Tv/EpisodeService.cs:166-169).
        // Returns all chapters that reference a specific ChapterFile.Id. Pass-through to
        // `IChapterRepository.GetChapterByFileId`. Required by the EpisodeFileDeletedEvent
        // handler analog and the file-rename / import paths.
        List<Chapter> GetChaptersByFileId(int fileId);

        // Plan 06-09 (Rule 3) — paged variant for the V5 Wanted/Missing controller. Sibling of
        // TV's `IEpisodeService.EpisodesWithoutFiles(PagingSpec, bool includeSpecials)`. The
        // monitored filter is APPLIED INSIDE the paging spec by the controller (so callers can
        // opt out via `monitored=false` query). The repository walks `Chapter.ChapterFileId IS NULL`
        // and applies the spec's FilterExpressions on top.
        PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec);

        void UpdateChapter(Chapter chapter);
        void SetChapterMonitored(int chapterId, bool monitored);

        // Sonarr divergence: bulk overload added in Phase 7 Plan 07-01 per D-07 — see DIVERGENCE.md.
        // Role-match analog: IEpisodeService.SetMonitored(IEnumerable<int>, bool). Consumed by
        // PUT /api/v5/chapter/monitor (ChapterController.SetChaptersMonitored). Per RESEARCH
        // Pitfall 4 ordering invariant, the implementation persists the DB write FIRST and then
        // publishes one ChapterUpdatedEvent per affected id (drives the SignalR `chapter` push).
        void SetChaptersMonitored(IEnumerable<int> chapterIds, bool monitored);

        void InsertMany(List<Chapter> chapters);
        void UpdateMany(List<Chapter> chapters);
        void DeleteMany(List<Chapter> chapters);

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-07) — sibling of TV's
        // `IEpisodeService.UpdateLastSearchTime(List<Episode>)` (Tv/EpisodeService.cs:199-202).
        // Focused setter via `IChapterRepository.SetFields(chapters, c => c.LastSearchTime)` to
        // avoid touching unrelated columns. Consumed by ChapterSearchService /
        // MissingChapterSearchService to record search history per chapter so subsequent polls
        // can skip recently-searched rows (avoids indexer rate-limit pressure).
        void UpdateLastSearchTime(List<Chapter> chapters);
    }
}
