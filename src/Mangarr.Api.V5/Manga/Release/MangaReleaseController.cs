using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Release/ReleaseController.cs (lines 26-343).
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
    //   * The Grab POST routes through IProcessMangaDownloadDecisions.ProcessDecision (sonarr-
    //     consistency-audit F-02 fix 2026-05-06) — same Pending/Rejected/Failed bucketing as the
    //     batch ProcessDecisions path used by ChapterSearchService + MangaSearchService. The
    //     service hands the cached RemoteChapter into the manga download pipeline; the grab POST
    //     surfaces a Rejected/Skipped decision as a 404 (Phase 40 D-10) rather than greening the
    //     UI button on a grab that queued nothing.
    [V5ApiController("manga/release")]
    public class MangaReleaseController : Controller
    {
        private readonly IMangaSearchForReleases _releaseSearchService;
        private readonly IProcessMangaDownloadDecisions _processDownloadDecisions;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IChapterSynthesisService _chapterSynthesisService;
        private readonly Logger _logger;

        private readonly ICached<RemoteChapter> _remoteChapterCache;

        public MangaReleaseController(IMangaSearchForReleases releaseSearchService,
                                      IProcessMangaDownloadDecisions processDownloadDecisions,
                                      IMangaService mangaService,
                                      IChapterService chapterService,
                                      IChapterSynthesisService chapterSynthesisService,
                                      ICacheManager cacheManager,
                                      Logger logger)
        {
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _chapterSynthesisService = chapterSynthesisService;
            _logger = logger;

            _remoteChapterCache = cacheManager.GetCache<RemoteChapter>(GetType(), "remoteChapters");
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<Results<Ok<List<MangaReleaseResource>>, BadRequest>> GetReleases(int? chapterId, int? mangaId)
        {
            // PIPELINE-01 Interactive Search — fans out to all enabled manga indexers via
            // IMangaSearchForReleases (Plan 06-06), runs Phase 5 MangaDownloadDecisionMaker,
            // and returns the ranked list (Approved + Rejected) for the React modal to render.
            //
            // Two scopes supported (mirrors TV ReleaseController's ?episodeId vs ?seriesId&seasonNumber):
            //   ?chapterId={id}  — single-chapter search; ChapterSearchCriteria
            //   ?mangaId={id}    — whole-manga search; MangaSearchCriteria across the manga's chapters
            //
            // Phase 7 D-10 frontend (`MangaDetails.tsx` Search tab) wires `<InteractiveSearch
            // type="manga" searchPayload={{ mangaId }} />`, which routes through
            // `useReleases.getReleasePath` to GET `/api/v5/manga/release?mangaId=<id>`. The
            // mangaId branch was missing in Phase 6 Plan 06-09 (only chapterId shipped) — the
            // Search tab silently returned BadRequest "Chapter not found" before this overload.
            try
            {
                if (chapterId.HasValue)
                {
                    var chapter = _chapterService.GetChapter(chapterId.Value);
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
                    return TypedResults.Ok(MapDecisions(decisions));
                }

                if (mangaId.HasValue)
                {
                    var manga = _mangaService.GetManga(mangaId.Value);
                    if (manga == null)
                    {
                        throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Manga not found");
                    }

                    var chapters = _chapterService.GetChaptersByManga(mangaId.Value) ?? new List<NzbDrone.Core.Manga.Chapter>();

                    var criteria = new MangaSearchCriteria
                    {
                        Manga = manga,
                        Chapters = chapters,
                        UserInvokedSearch = true,
                        InteractiveSearch = true,

                        // User-invoked whole-manga search: include every chapter the indexer can
                        // fetch (not just monitored). The Decision Engine still rejects rows that
                        // don't match a monitored chapter under normal RSS sync; for an interactive
                        // search the user's intent is "show me everything that exists for this manga".
                        MonitoredChaptersOnly = false
                    };

                    var decisions = await _releaseSearchService.MangaSearch(criteria);
                    return TypedResults.Ok(MapDecisions(decisions));
                }

                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "chapterId or mangaId must be provided");
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
            // PIPELINE-01 Grab — looks up the cached RemoteChapter by Guid and routes it through
            // IProcessMangaDownloadDecisions.ProcessDecision. A Rejected/Skipped decision is surfaced
            // as a 404 (Phase 40 D-10) so the UI never greens a grab that queued nothing.
            var remoteChapter = _remoteChapterCache.Find(GetCacheKey(resource));
            if (remoteChapter == null)
            {
                _logger.Debug("Couldn't find requested manga release in cache, cache timeout probably expired.");
                throw new NzbDroneClientException(HttpStatusCode.NotFound,
                    "Couldn't find requested release in cache, try searching again");
            }

            // Phase 40 RECON-03 / D-04: on-grab synthesis runs BEFORE the decision so the
            // freshly-synthesized Chapter row makes the decision qualify. A manual grab of a
            // chapter the MangaDex metadata catalog never enumerated (but the gateway exposes)
            // would otherwise be rejected for having no local Chapter row. This is belt-and-
            // suspenders with the Part A captured-result 404 backstop (D-11): synthesis closes
            // the genuine gap; the 404 guard below still surfaces any remaining Rejected/Skipped.
            //
            // WR-03: synthesis is a best-effort side effect — a transient DB error (or a
            // UNIQUE-violation under the WR-02 refresh race) must NOT blow up an otherwise
            // grabbable release with an unhandled 500. WR-01: re-hydrate the cached
            // RemoteChapter.Chapters from the resolved rows — the cache was built at search
            // time when the uncataloged number had no row, so IsQualifiedReport (which requires
            // Chapters.Any()) would reject the grab unless we merge the just-synthesized row in.
            try
            {
                var ensured = _chapterSynthesisService.SynthesizeForGrab(remoteChapter);
                foreach (var chapter in ensured)
                {
                    if (remoteChapter.Chapters.All(c => c.Id != chapter.Id))
                    {
                        remoteChapter.Chapters.Add(chapter);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "On-grab chapter synthesis failed for release '{0}'; continuing with grab.", remoteChapter);
            }

            try
            {
                // F-02 fix (sonarr-consistency-audit 2026-05-06): route through
                // IProcessMangaDownloadDecisions.ProcessDecision — same Pending/Rejected/Failed
                // bucketing as the batch ProcessDecisions path. The service applies the
                // qualified-report gate, the TemporarilyRejected → Pending(Delay) routing, and
                // the shared ProcessDecisionInternal download dispatch.
                //
                // Phase 40 D-10: capture the ProcessedDecisionResult (previously discarded) and
                // surface a Rejected/Skipped grab as a 404 + Warn log — mirrors the cache-miss
                // throw above so the UI button never greens on a grab that queued nothing.
                var decision = new NzbDrone.Core.DecisionEngine.Manga.MangaDownloadDecision(remoteChapter);
                var result = await _processDownloadDecisions.ProcessDecision(decision, downloadClientId: null);
                if (result is ProcessedDecisionResult.Rejected or ProcessedDecisionResult.Skipped)
                {
                    _logger.Warn("Manga grab for release '{0}' returned {1}; nothing queued.", remoteChapter, result);
                    throw new NzbDroneClientException(HttpStatusCode.NotFound,
                        "Release could not be grabbed. Try searching again.");
                }
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
