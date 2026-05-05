using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MangaStats
{
    public interface IMangaStatisticsService
    {
        List<MangaStatistics> MangaStatistics();
        MangaStatistics MangaStatistics(int mangaId);
    }

    // Audit no-sibling/SeriesStatisticsService (Phase 8 11-03). Mirrors TV
    // SeriesStatisticsService.cs as a thin wrapper around IMangaStatisticsRepository.
    // Diverges from TV in two ways:
    //   * No IQualityProfileService injection — MangaStatistics dropped
    //     EpisodeFileQualities (Quality replaced by CustomFormats per Phase 5).
    //   * No SeasonStatistics → MangaStatistics rollup — MangaStatisticsRepository
    //     already aggregates by MangaId only (no Seasons join per D-13), so the
    //     service is pure delegation rather than the TV GroupBy + MapSeriesStatistics
    //     fold. Single-MangaId queries return the first row or an empty
    //     MangaStatistics shell to mirror SeriesStatisticsService's null-guard at
    //     SeriesStatisticsService.cs:51-54.
    public class MangaStatisticsService : IMangaStatisticsService
    {
        private readonly IMangaStatisticsRepository _mangaStatisticsRepository;

        public MangaStatisticsService(IMangaStatisticsRepository mangaStatisticsRepository)
        {
            _mangaStatisticsRepository = mangaStatisticsRepository;
        }

        public List<MangaStatistics> MangaStatistics()
        {
            return _mangaStatisticsRepository.MangaStatistics();
        }

        public MangaStatistics MangaStatistics(int mangaId)
        {
            var stats = _mangaStatisticsRepository.MangaStatistics(mangaId);

            if (stats == null || stats.Count == 0)
            {
                return new MangaStatistics();
            }

            return stats.First();
        }
    }
}
