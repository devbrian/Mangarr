using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-12 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/EpisodeSearchService.Execute(EpisodeSearchCommand)
    // lines 110-119.
    //
    // Per-chapter dispatch (Interactive Search + auto-retry consumer per D-12).
    // Each ChapterSearchCommand carries N chapter ids; this service walks them and
    // fires one ChapterSearch fan-out per id, then hands the ranked decisions to
    // IProcessMangaDownloadDecisions for the grab + Pending/Rejected bucketing
    // (Phase 8 Plan 99-06 — closes gap-01 family).
    //
    // Phase 15 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class ChapterSearchService : IExecute<ChapterSearchCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly IProcessMangaDownloadDecisions _processDownloadDecisions;
        private readonly Logger _logger;

        public ChapterSearchService(IMangaService mangaService,
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

        public void Execute(ChapterSearchCommand message)
        {
            if (message.ChapterIds == null || message.ChapterIds.Count == 0)
            {
                _logger.Debug("ChapterSearchCommand received with no ChapterIds; nothing to search");
                return;
            }

            var userInvoked = message.Trigger == CommandTrigger.Manual;

            foreach (var chapterId in message.ChapterIds)
            {
                var chapter = _chapterService.GetChapter(chapterId);
                if (chapter == null)
                {
                    _logger.Warn("Chapter {0} not found; skipping search", chapterId);
                    continue;
                }

                var manga = _mangaService.GetManga(chapter.MangaId);
                if (manga == null)
                {
                    _logger.Warn("Manga {0} for chapter {1} not found; skipping search", chapter.MangaId, chapterId);
                    continue;
                }

                var criteria = new ChapterSearchCriteria
                {
                    Manga = manga,
                    Chapters = new List<NzbDrone.Core.Manga.Chapter> { chapter },
                    MonitoredChaptersOnly = false,
                    UserInvokedSearch = userInvoked
                };

                var decisions = _releaseSearchService.ChapterSearch(criteria).GetAwaiter().GetResult();
                var processed = _processDownloadDecisions.ProcessDecisions(decisions).GetAwaiter().GetResult();

                _logger.ProgressInfo(
                    "Chapter search completed for {0} Ch.{1:0.###}. {2} reports downloaded.",
                    manga.Title,
                    chapter.ChapterNumber,
                    processed.Grabbed.Count);
            }
        }
    }
}
