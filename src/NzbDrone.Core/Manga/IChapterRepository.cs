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

        // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-01) — sibling of TV's
        // `IEpisodeRepository.GetEpisodesBySeriesIds(List<int> seriesIds)`. Bulk get-by-multiple-parent-IDs
        // for batch operations (e.g. multi-manga wanted-search, multi-manga rescan).
        List<Chapter> GetChaptersByMangaIds(List<int> mangaIds);

        List<Chapter> GetSyntheticByMangaId(int mangaId);

        // Phase 8 audit (EpisodeRepository-vs-ChapterRepository.md gap-02) — sibling of TV's
        // `IEpisodeRepository.GetEpisodeByFileId(int fileId)`. Returns all chapters that reference a
        // specific ChapterFile.Id. Manga's `ChapterFileId` is nullable (vs. TV's int sentinel `0`),
        // so the predicate compares against the int value via the Nullable HasValue path. List
        // shape mirrors TV — multiple chapter rows can theoretically reference the same file
        // during import/move pipelines (Phase 4 archiver territory).
        List<Chapter> GetChapterByFileId(int fileId);

        // Phase 6 D-09 — monitored chapter rows with no ChapterFile imported (Wanted/Missing feed).
        List<Chapter> AllMissingMonitoredChapters();

        // Plan 06-09 — paged variant for the V5 Wanted/Missing controller. Sibling of TV's
        // `IEpisodeRepository.EpisodesWithoutFiles(PagingSpec, bool includeSpecials)`. The monitored
        // filter is applied at the controller layer via PagingSpec.FilterExpressions; this method
        // narrows to rows with `ChapterFileId IS NULL` and lets the spec layer apply the rest.
        PagingSpec<Chapter> ChaptersWithoutFiles(PagingSpec<Chapter> pagingSpec);

        // Phase 8 audit (no-sibling/EpisodeCutoffService.md + gap-13) — paged Cutoff-Unmet feed.
        // Sibling of TV's `IEpisodeRepository.EpisodesWhereCutoffUnmet(PagingSpec, qualitiesBelowCutoff, bool)`.
        // Manga-shape divergence: cutoff axes are TranslationProfile language preference + CustomFormatProfile
        // score thresholds (Phase 5 D-01 + D-07) instead of TV's QualityProfile cutoff. Caller (ChapterCutoffService)
        // pre-computes the "below cutoff" id lists from the profile services and passes them in.
        PagingSpec<Chapter> ChaptersWhereCutoffUnmet(PagingSpec<Chapter> pagingSpec,
                                                    List<int> belowCutoffTranslationProfileIds,
                                                    List<int> belowCutoffCustomFormatProfileIds);

        void SetMonitored(IEnumerable<int> ids, bool monitored);
    }
}
