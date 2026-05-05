using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
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
    // delegates to IMangaSearchForReleases (fan-out + decision maker), and logs the result.
    //
    // The grab path is intentionally NOT wired here — Plan 06-07 ImportApprovedChapters
    // owns the staging-CBZ-to-library transition and Plan 06-08 AutoRetryOrchestrator
    // owns the failed-grab redirect. This service's contract is "search and rank";
    // approved decisions carry RemoteChapter and surface to the consumer for grabbing.
    //
    // Phase 8 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class MangaSearchService : IExecute<MangaSearchCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly Logger _logger;

        public MangaSearchService(IMangaService mangaService,
                                  IChapterService chapterService,
                                  IMangaSearchForReleases releaseSearchService,
                                  Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _releaseSearchService = releaseSearchService;
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

                // Sonarr parity (Phase 8 audit gap-04): mirror SeriesSearchService.cs:74 log shape —
                // report grabbed-count semantics. The manga grab path (Plan 06-07/08) does not run
                // here, so the closest available semantic is approved-decision count (highest-fidelity
                // proxy until gap-01 lands a true grab counter); the trailing total/breakdown is
                // dropped to match TV's terse "{N} reports downloaded." shape.
                var downloadedCount = decisions.Count(d => d.Approved);

                _logger.ProgressInfo("Manga search completed. {0} reports downloaded.", downloadedCount);
            }
        }
    }
}
