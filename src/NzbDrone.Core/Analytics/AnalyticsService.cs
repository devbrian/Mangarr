using System;
using System.Linq;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History.Manga;

namespace NzbDrone.Core.Analytics
{
    public interface IAnalyticsService
    {
        bool IsEnabled { get; }
        bool InstallIsActive { get; }
    }

    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — IHistoryService (TV) replaced
    // with IChapterHistoryService (manga peer); EpisodeHistory paging spec replaced with
    // ChapterHistory paging.
    public class AnalyticsService : IAnalyticsService
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IChapterHistoryService _historyService;

        public AnalyticsService(IChapterHistoryService historyService, IConfigFileProvider configFileProvider)
        {
            _configFileProvider = configFileProvider;
            _historyService = historyService;
        }

        public bool IsEnabled => (_configFileProvider.AnalyticsEnabled && RuntimeInfo.IsProduction) || RuntimeInfo.IsDevelopment;

        public bool InstallIsActive
        {
            get
            {
                var lastRecord = _historyService.Paged(new PagingSpec<ChapterHistory>() { Page = 0, PageSize = 1, SortKey = "date", SortDirection = SortDirection.Descending }, null as int[]);
                var monthAgo = DateTime.UtcNow.AddMonths(-1);

                return lastRecord.Records.Any(v => v.Date > monthAgo);
            }
        }
    }
}
