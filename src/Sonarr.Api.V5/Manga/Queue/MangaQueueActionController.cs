using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using Sonarr.Api.V5.Queue;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-10 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Queue/QueueActionController.cs:1-58
    // Preserves: bare Controller base, [HttpPost("grab/{id:int}")] + [HttpPost("grab/bulk")]
    //   action templates, async grab pattern, [Consumes("application/json")] on bulk.
    // Diverges: route literal "manga/queue" shared with MangaQueueController (RESEARCH §Pitfall 2 +
    //   §Pitfall 6 coexistence — discriminated by action templates: MangaQueueController owns
    //   [HttpGet] + [RestDeleteById] while this owns [HttpPost("grab/...")]); IPendingReleaseService
    //   → IMangaPendingReleaseService; pendingRelease.RemoteEpisode →
    //   pendingRelease.RemoteChapter.ToRemoteEpisodeShim() per Plan 06-09 MangaReleaseController.cs:175
    //   shim pattern (Parser/Manga/Model/RemoteChapterExtensions.cs:17). Reuses TV's QueueBulkResource
    //   (`{ Ids: int[] }` — domain-neutral; no manga-specific peer needed).
    // Phase 15 collapse: TV peer deletion + namespace rename + IDownloadService unification drops the
    //   ToRemoteEpisodeShim() call and collapses this to QueueActionController.
    [V5ApiController("manga/queue")]
    public class MangaQueueActionController : Controller
    {
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly IDownloadService _downloadService;

        public MangaQueueActionController(IMangaPendingReleaseService pendingReleaseService,
                                          IDownloadService downloadService)
        {
            _pendingReleaseService = pendingReleaseService;
            _downloadService = downloadService;
        }

        [HttpPost("grab/{id:int}")]
        public async Task<NoContent> Grab([FromRoute] int id)
        {
            var pendingRelease = _pendingReleaseService.FindPendingQueueItem(id);

            if (pendingRelease == null)
            {
                throw new NotFoundException();
            }

            await _downloadService.DownloadReport(pendingRelease.RemoteChapter.ToRemoteEpisodeShim(), null);

            return TypedResults.NoContent();
        }

        [HttpPost("grab/bulk")]
        [Consumes("application/json")]
        public async Task<NoContent> Grab([FromBody] QueueBulkResource resource)
        {
            foreach (var id in resource.Ids)
            {
                var pendingRelease = _pendingReleaseService.FindPendingQueueItem(id);

                if (pendingRelease == null)
                {
                    throw new NotFoundException();
                }

                await _downloadService.DownloadReport(pendingRelease.RemoteChapter.ToRemoteEpisodeShim(), null);
            }

            return TypedResults.NoContent();
        }
    }
}
