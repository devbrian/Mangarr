using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending.Manga;
using Mangarr.Api.V5.Queue;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Queue
{
    // Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-10 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Queue/QueueActionController.cs:1-58
    // Preserves: bare Controller base, [HttpPost("grab/{id:int}")] + [HttpPost("grab/bulk")]
    //   action templates, async grab pattern, [Consumes("application/json")] on bulk.
    // Diverges: route literal "manga/queue" shared with MangaQueueController (RESEARCH §Pitfall 2 +
    //   §Pitfall 6 coexistence — discriminated by action templates: MangaQueueController owns
    //   [HttpGet] + [RestDeleteById] while this owns [HttpPost("grab/...")]); IPendingReleaseService
    //   → IMangaPendingReleaseService; pendingRelease.RemoteEpisode →
    //   pendingRelease.RemoteChapter consumed directly by IMangaDownloadService.DownloadReport
    //   (Phase 15 Wave (A) W-4 rebind 2026-05-07; pre-Wave-(A) used the
    //   RemoteChapter.ToRemoteEpisodeShim() bridge into IDownloadService). Reuses TV's
    //   QueueBulkResource (`{ Ids: int[] }` — domain-neutral; no manga-specific peer needed).
    // Phase 15 Wave (C) collapse: TV peer deletion + namespace rename collapses this to
    //   QueueActionController.
    [V5ApiController("manga/queue")]
    public class MangaQueueActionController : Controller
    {
        private readonly IMangaPendingReleaseService _pendingReleaseService;
        private readonly IMangaDownloadService _downloadService;

        public MangaQueueActionController(IMangaPendingReleaseService pendingReleaseService,
                                          IMangaDownloadService downloadService)
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

            await _downloadService.DownloadReport(pendingRelease.RemoteChapter, null);

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

                await _downloadService.DownloadReport(pendingRelease.RemoteChapter, null);
            }

            return TypedResults.NoContent();
        }
    }
}
