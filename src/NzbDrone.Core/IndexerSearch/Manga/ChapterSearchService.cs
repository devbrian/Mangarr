using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
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
    // fires one ChapterSearch fan-out per id. Decisions are returned; the grab
    // path lives downstream (Plan 06-07/08).
    //
    // Phase 8 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class ChapterSearchService : IExecute<ChapterSearchCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly Logger _logger;

        public ChapterSearchService(IMangaService mangaService,
                                    IChapterService chapterService,
                                    IMangaSearchForReleases releaseSearchService,
                                    Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _releaseSearchService = releaseSearchService;
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
                var approved = decisions.Count(d => d.Approved);

                _logger.ProgressInfo(
                    "Chapter search completed for {0} Ch.{1:0.###}. {2}/{3} releases approved",
                    manga.Title,
                    chapter.ChapterNumber,
                    approved,
                    decisions.Count);
            }
        }
    }
}
