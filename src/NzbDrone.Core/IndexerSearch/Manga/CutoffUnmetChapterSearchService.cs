using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Queue.Manga;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/EpisodeSearchService.Execute(CutoffUnmetEpisodeSearchCommand)
    // (lines 163-195 — PagingSpec → cutoff query → queue dedup → per-group dispatch).
    //
    // SIBLING-SERVICE per Phase 6 D-09: this is its own class implementing
    // IExecute<CutoffUnmetChapterSearchCommand> rather than extending
    // MissingChapterSearchService. The two sweep different chapter sets
    // (missing-file vs. below-cutoff) and the symbol shape stays parallel to TV.
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
    // V5 controller wiring (Sonarr.Api.V5/Manga/Wanted/CutoffChaptersController) +
    // frontend Wanted/CutoffUnmet manga page are deferred to follow-up plans
    // (out of cluster scope per Phase 8 cluster 07-search-grab — backfill is
    // command + handler only).
    //
    // Phase 8 cleanup: collapse with EpisodeSearchService when Tv/ deletes.
    public class CutoffUnmetChapterSearchService : IExecute<CutoffUnmetChapterSearchCommand>
    {
        private readonly IChapterCutoffService _chapterCutoffService;
        private readonly IMangaService _mangaService;
        private readonly IMangaQueueService _mangaQueueService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public CutoffUnmetChapterSearchService(IChapterCutoffService chapterCutoffService,
                                               IMangaService mangaService,
                                               IMangaQueueService mangaQueueService,
                                               IManageCommandQueue commandQueueManager,
                                               Logger logger)
        {
            _chapterCutoffService = chapterCutoffService;
            _mangaService = mangaService;
            _mangaQueueService = mangaQueueService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Execute(CutoffUnmetChapterSearchCommand message)
        {
            // PagingSpec mirrors TV's EpisodeSearchService.Execute(CutoffUnmetEpisodeSearchCommand)
            // shape verbatim — ChapterRepository.ChaptersWhereCutoffUnmet narrows the paged
            // query to chapters under translation/custom-format profiles flagged below-cutoff
            // (see ChapterCutoffService — Phase 8 audit no-sibling/EpisodeCutoffService.md).
            var pagingSpec = new PagingSpec<Chapter>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            if (message.MangaId.HasValue)
            {
                var mangaId = message.MangaId.Value;
                var manga = _mangaService.GetManga(mangaId);
                if (manga == null)
                {
                    _logger.Debug("Manga {0} not found; nothing to search", mangaId);
                    return;
                }

                pagingSpec.FilterExpressions.Add(c => c.MangaId == mangaId);
            }

            // Manga.Monitored cross-filter is enforced by the repository layer (mirrors
            // EpisodeRepository.EpisodesWhereCutoffUnmet); message.Monitored is the chapter-level
            // filter and is currently advisory because the cutoff query already implies
            // monitored chapters. Preserved for symbol parity with TV's command shape.
            var chapters = _chapterCutoffService.ChaptersWhereCutoffUnmet(pagingSpec).Records.ToList();

            // Dedup against in-flight queue rows (Plan 06-05 substrate — BL-01 GUARD).
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
                _logger.Debug("No cutoff-unmet monitored chapters to search");
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
                "Cutoff-unmet chapter search dispatched: {0} manga(s) covering {1} chapter(s)",
                grouped.Count,
                grouped.Sum(g => g.Count()));
        }
    }
}
