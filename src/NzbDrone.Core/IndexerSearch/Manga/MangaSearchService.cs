using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/EpisodeSearchService.Execute(EpisodeSearchCommand)
    // lines 110-119 (per-message dispatch shape).
    //
    // D-06: bulk MangaIds (NOT per-chapter fan-out). For each requested manga, builds a
    // MangaSearchCriteria carrying ONLY monitored chapters that lack a ChapterFile,
    // delegates to IMangaSearchForReleases (fan-out + decision maker), then hands the
    // ranked decisions to IProcessMangaDownloadDecisions for the grab + Pending/Rejected
    // bucketing (Phase 8 Plan 99-06 — closes gap-01 family). Closes the Wanted/Missing
    // sweep + Add-Manga search-on-add flows that previously found-but-did-not-grab.
    //
    // Phase 15 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class MangaSearchService : IExecute<MangaSearchCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly IProcessMangaDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public MangaSearchService(IMangaService mangaService,
                                  IChapterService chapterService,
                                  IMangaSearchForReleases releaseSearchService,
                                  IProcessMangaDownloadDecisions processDownloadDecisions,
                                  Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _logger = logger;
        }

        public void Execute(MangaSearchCommand message)
        {
            if (message.MangaIds == null || message.MangaIds.Count == 0)
            {
                _logger.Debug("MangaSearchCommand received with no MangaIds; nothing to search");
                return;
            }

            foreach (var mangaId in message.MangaIds)
            {
                var manga = _mangaService.GetManga(mangaId);
                if (manga == null)
                {
                    _logger.Warn("Manga {0} not found; skipping search", mangaId);
                    continue;
                }

                // Filter: monitored chapters with no imported file.
                // CONTEXT.md D-04: IsSynthetic=true rows are NOT filtered out — they're
                // treated identically to real-feed chapters for Wanted/search purposes.
                var chapters = _chapterService.GetChaptersByManga(mangaId)
                    .Where(c => c.Monitored && c.ChapterFileId == null)
                    .ToList();

                var criteria = new MangaSearchCriteria
                {
                    Manga = manga,
                    Chapters = chapters,
                    MonitoredChaptersOnly = true,
                    UserInvokedSearch = message.UserInvokedSearch || message.Trigger == CommandTrigger.Manual
                };

                var decisions = _releaseSearchService.MangaSearch(criteria).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo("Manga search completed. {0} reports downloaded.", processed.Grabbed.Count);
            }
        }
    }
}
