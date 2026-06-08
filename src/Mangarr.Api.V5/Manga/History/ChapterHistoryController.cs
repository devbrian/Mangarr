using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http;
using Mangarr.Http.Extensions;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;

namespace Mangarr.Api.V5.Manga.History
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/History/HistoryController.cs.
    //
    // HISTORY-01..03: paged history listing + retry endpoint that pushes ChapterSearchCommand.
    //
    // Manga sibling preserves: [V5ApiController] route attribute (admin X-Api-Key requirement
    // per RESEARCH §Security Domain V4); PagingRequestResource → MapToPagingSpec; filter-
    // expression chain pattern; TypedResults.Ok response shape; HISTORY-03 retry endpoint.
    //
    // Manga sibling diverges from HistoryController:
    //   * Inject IChapterHistoryService (Plan 06-03), IManageCommandQueue (push retry).
    //   * Drop quality filter (manga has no quality model per Phase 5 D-04).
    //   * Add mangaIds / chapterId filters (HISTORY-02 per-Manga/Chapter scope).
    //   * Retry POST endpoint pushes ChapterSearchCommand (Plan 06-06) instead of
    //     IFailedDownloadService.MarkAsFailed — manga's auto-retry orchestrator (Plan 06-08)
    //     handles the fail+blocklist flow when a download terminally fails. The orchestrator
    //     mirrors Sonarr's RedownloadFailedDownloadService (no retry budget — the blocklist
    //     bounds the loop), so Retry from History is the user's manual escape hatch for the
    //     terminal case where every candidate release has been blocklisted (per HISTORY-03).
    //   * includeSubresources hydrates Manga/Chapter via service-layer Get (Plan 06-03 D-21
    //     decision: ChapterHistoryRepository.GetPaged does NOT JOIN — controller hydrates).
    //
    // Phase 8 cleanup: collapse with HistoryController when Tv/ deletes.
    [V5ApiController("manga/history")]
    public class ChapterHistoryController : Controller
    {
        private readonly IChapterHistoryService _historyService;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public ChapterHistoryController(IChapterHistoryService historyService,
                                        IMangaService mangaService,
                                        IChapterService chapterService,
                                        IManageCommandQueue commandQueueManager,
                                        Logger logger)
        {
            _historyService = historyService;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<ChapterHistoryResource>> GetHistory([FromQuery] PagingRequestResource paging,
                                                                    [FromQuery(Name = "eventType")] int[]? eventTypes,
                                                                    int? chapterId,
                                                                    string? downloadId,
                                                                    [FromQuery] int[]? mangaIds = null,
                                                                    [FromQuery] int[]? languages = null,
                                                                    [FromQuery] ChapterHistorySubresource[]? includeSubresources = null)
        {
            var pagingResource = new PagingResource<ChapterHistoryResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<ChapterHistoryResource, ChapterHistory>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "date",
                    "sourceTitle"
                },
                "date",
                SortDirection.Descending);

            if (eventTypes != null && eventTypes.Any())
            {
                pagingSpec.FilterExpressions.Add(v => eventTypes.Contains((int)v.EventType));
            }

            if (chapterId.HasValue)
            {
                pagingSpec.FilterExpressions.Add(h => h.ChapterId == chapterId.Value);
            }

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                pagingSpec.FilterExpressions.Add(h => h.DownloadId == downloadId);
            }

            if (mangaIds != null && mangaIds.Any())
            {
                pagingSpec.FilterExpressions.Add(h => mangaIds.Contains(h.MangaId));
            }

            var includeManga = includeSubresources?.Contains(ChapterHistorySubresource.Manga) ?? false;
            var includeChapter = includeSubresources?.Contains(ChapterHistorySubresource.Chapter) ?? false;

            var resource = pagingSpec.ApplyToPage(
                spec => _historyService.Paged(spec, languages ?? Array.Empty<int>()),
                h => MapToResource(h, includeManga, includeChapter));

            return TypedResults.Ok(resource);
        }

        [HttpPost("failed/{id:int}/retry")]
        public NoContent RetryFailed([FromRoute] int id)
        {
            // HISTORY-03 — manual retry escape hatch after the D-13 auto-retry budget exhausts.
            // Pushes a single-element ChapterSearchCommand (Plan 06-06) for the failed chapter;
            // the decision engine will skip the now-blocklisted release (Plan 06-04 D-19) and
            // grab next-best.
            var history = _historyService.Get(id);
            if (history == null)
            {
                throw new NotFoundException();
            }

            _commandQueueManager.Push(new ChapterSearchCommand(new List<int> { history.ChapterId }));
            return TypedResults.NoContent();
        }

        private ChapterHistoryResource MapToResource(ChapterHistory model, bool includeManga, bool includeChapter)
        {
            var resource = model.ToResource()!;

            if (includeManga)
            {
                var manga = _mangaService.GetManga(model.MangaId);
                if (manga != null)
                {
                    resource.Manga = new MangaSubresource
                    {
                        Id = manga.Id,
                        Title = manga.Title
                    };
                }
            }

            if (includeChapter)
            {
                var chapter = _chapterService.GetChapter(model.ChapterId);
                if (chapter != null)
                {
                    resource.Chapter = new ChapterSubresource
                    {
                        Id = chapter.Id,
                        MangaId = chapter.MangaId,
                        ChapterNumber = chapter.ChapterNumber,
                        Title = chapter.Title
                    };
                }
            }

            return resource;
        }
    }

    public enum ChapterHistorySubresource
    {
        Manga,
        Chapter
    }
}
