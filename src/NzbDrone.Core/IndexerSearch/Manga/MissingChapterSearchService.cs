using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-09 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/EpisodeSearchService.Execute(MissingEpisodeSearchCommand)
    // (lines 121-161 — walking pattern + queue dedup).
    //
    // D-09 mandates ONE MangaSearchCommand per affected Manga (NOT per-chapter
    // fan-out). Per-source rate budget naturally respected because the command
    // queue serializes them. CONTEXT.md D-04 honored: IsSynthetic=true rows are
    // included in the grouping (no special-case filter).
    //
    // BL-01 GUARD: queue dedup uses _mangaQueueService.GetMangaQueue() (Plan 06-05
    // — D-20) and matches against MangaQueueItem.RemoteChapter.Chapters.Id, NOT
    // TV's IQueueService.GetQueue() whose Episodes.Id collides with Chapter.Id.
    //
    // Phase 8 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class MissingChapterSearchService : IExecute<MissingChapterSearchCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IMangaQueueService _mangaQueueService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public MissingChapterSearchService(IMangaService mangaService,
                                           IChapterService chapterService,
                                           IMangaQueueService mangaQueueService,
                                           IManageCommandQueue commandQueueManager,
                                           Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _mangaQueueService = mangaQueueService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(MissingChapterSearchCommand message)
        {
            var monitored = message.Monitored;
            List<Chapter> chapters;

            if (message.MangaId.HasValue)
            {
                var manga = _mangaService.GetManga(message.MangaId.Value);
                if (manga == null)
                {
                    _logger.Debug("Manga {0} not found; nothing to search", message.MangaId.Value);
                    return;
                }

                chapters = _chapterService.GetChaptersByManga(message.MangaId.Value)
                    .Where(c => c.Monitored == monitored && c.ChapterFileId == null)
                    .ToList();
            }
            else
            {
                // AllMissingMonitoredChapters returns Monitored == true && ChapterFileId IS NULL.
                // Manga.Monitored filtering is applied at the consumer (here) per the
                // IChapterService contract comment.
                chapters = _chapterService.AllMissingMonitoredChapters();
            }

            // Dedup against in-flight queue rows (Plan 06-05 substrate).
            var queuedChapterIds = _mangaQueueService.GetMangaQueue()
                .Where(q => q.RemoteChapter?.Chapters != null)
                .SelectMany(q => q.RemoteChapter.Chapters.Select(c => c.Id))
                .ToHashSet();

            // D-09: group by MangaId, push one MangaSearchCommand per Manga.
            // D-04: NO synthetic filter — IsSynthetic rows are treated identically.
            var grouped = chapters
                .Where(c => !queuedChapterIds.Contains(c.Id))
                .GroupBy(c => c.MangaId)
                .ToList();

            if (grouped.Count == 0)
            {
                _logger.Debug("No missing monitored chapters to search");
                return;
            }

            var userInvoked = message.Trigger == CommandTrigger.Manual;

            foreach (var group in grouped)
            {
                _commandQueueManager.Push(
                    new MangaSearchCommand(new List<int> { group.Key }, userInvoked: userInvoked),
                    CommandPriority.Normal,
                    CommandTrigger.Unspecified);
            }

            _logger.Info(
                "Missing-chapter search dispatched: {0} manga(s) covering {1} missing chapter(s)",
                grouped.Count,
                grouped.Sum(g => g.Count()));
        }
    }
}
