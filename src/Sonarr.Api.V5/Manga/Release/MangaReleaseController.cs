using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using Sonarr.Http;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Sonarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Release/ReleaseController.cs (lines 26-343).
    //
    // PIPELINE-01: Interactive Search modal endpoint (GET) + Grab POST.
    //
    // Manga sibling preserves: [V5ApiController] route attribute; ICached<RemoteChapter> to
    // round-trip the parsed report between the GET search and the POST grab; ranked decision
    // mapping; SearchFailedException → BadRequest mapping.
    //
    // Manga sibling diverges from ReleaseController:
    //   * Inject IMangaSearchForReleases (Plan 06-06) instead of ISearchForReleases.
    //   * Cache RemoteChapter (Phase 5 type) instead of RemoteEpisode.
    //   * No quality model / no SeasonSearch path / no TV scene-mapping logic.
    //   * The Grab POST builds a thin RemoteEpisode shim around the cached RemoteChapter and
    //     delegates to the existing IDownloadService.DownloadReport — Phase 4 D-10's
    //     `Protocol == DownloadProtocol.Http` early-return guard routes the manga-protocol
    //     release into InProcessImageDownloadClient. Mirrors the Phase 4 ChapterDownloadService
    //     wire-level adapter convention (uses remote.Series.Id as the manga id and
    //     remote.Episodes[0].Id as the chapter id).
    //
    // Phase 8 cleanup: collapse with ReleaseController when Tv/ deletes — the TV-side
    // `Protocol == DownloadProtocol.Http` early-return guard disappears with it; the manga
    // grab path becomes a direct call into a unified IDownloadService.
    [V5ApiController("manga/release")]
    public class MangaReleaseController : Controller
    {
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly IDownloadService _downloadService;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly Logger _logger;

        private readonly ICached<RemoteChapter> _remoteChapterCache;

        public MangaReleaseController(IMangaSearchForReleases releaseSearchService,
                                      IDownloadService downloadService,
                                      IMangaService mangaService,
                                      IChapterService chapterService,
                                      ICacheManager cacheManager,
                                      Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _downloadService = downloadService;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _logger = logger;

            _remoteChapterCache = cacheManager.GetCache<RemoteChapter>(GetType(), "remoteChapters");
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<Results<Ok<List<MangaReleaseResource>>, BadRequest>> GetReleases(int chapterId)
        {
            // PIPELINE-01 Interactive Search — fans out to all enabled manga indexers via
            // IMangaSearchForReleases.ChapterSearch (Plan 06-06), runs Phase 5
            // MangaDownloadDecisionMaker, and returns the ranked list (Approved + Rejected)
            // for the React modal to render.
            try
            {
                var chapter = _chapterService.GetChapter(chapterId);
                if (chapter == null)
                {
                    throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Chapter not found");
                }

                var manga = _mangaService.GetManga(chapter.MangaId);
                if (manga == null)
                {
                    throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Manga not found");
                }

                var criteria = new ChapterSearchCriteria
                {
                    Manga = manga,
                    Chapters = new List<NzbDrone.Core.Manga.Chapter> { chapter },
                    UserInvokedSearch = true,
                    InteractiveSearch = true,
                    MonitoredChaptersOnly = false
                };

                var decisions = await _releaseSearchService.ChapterSearch(criteria);

                var resources = MapDecisions(decisions);
                return TypedResults.Ok(resources);
            }
            catch (SearchFailedException ex)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
            }
            catch (NzbDroneClientException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Manga interactive search failed: {0}", ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        [HttpPost]
        [Consumes("application/json")]
        [Produces("application/json")]
        public async Task<Results<Ok<MangaReleaseResource>, NotFound>> DownloadRelease([FromBody] MangaReleaseResource resource)
        {
            // PIPELINE-01 Grab — looks up the cached RemoteChapter by Guid and delegates to
            // IDownloadService.DownloadReport. Phase 4 D-10 routes Protocol=Http into the
            // InProcessImageDownloadClient via the early-return guard in CompletedDownloadService.
            var remoteChapter = _remoteChapterCache.Find(GetCacheKey(resource));
            if (remoteChapter == null)
            {
                _logger.Debug("Couldn't find requested manga release in cache, cache timeout probably expired.");
                throw new NzbDroneClientException(HttpStatusCode.NotFound,
                    "Couldn't find requested release in cache, try searching again");
            }

            try
            {
                // Build the wire-level RemoteEpisode shim. Phase 4's InProcessImageDownloadClient
                // reads only `Release` (manifest fetch) plus `Series.Id` → MangaId + `Episodes[0].Id`
                // → ChapterId in ChapterDownloadService.EnqueueAsync. Mirrors the Phase 4 thin-shim
                // convention; collapses in Phase 8 when RemoteEpisode → RemoteChapter rename lands.
                var shim = remoteChapter.ToRemoteEpisodeShim();
                await _downloadService.DownloadReport(shim, downloadClientId: null);
            }
            catch (ReleaseDownloadException ex)
            {
                _logger.Error(ex, ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Getting release from indexer failed");
            }

            return TypedResults.Ok(resource);
        }

        private List<MangaReleaseResource> MapDecisions(List<NzbDrone.Core.DecisionEngine.Manga.MangaDownloadDecision> decisions)
        {
            var result = new List<MangaReleaseResource>(decisions.Count);
            foreach (var decision in decisions)
            {
                var resource = decision.ToResource(result.Count);
                if (decision.RemoteChapter?.Release?.Guid != null)
                {
                    _remoteChapterCache.Set(GetCacheKey(resource), decision.RemoteChapter, TimeSpan.FromMinutes(30));
                }

                result.Add(resource);
            }

            return result;
        }

        private static string GetCacheKey(MangaReleaseResource resource)
            => string.Concat(resource.IndexerId, "_", resource.Guid ?? string.Empty);
    }
}
