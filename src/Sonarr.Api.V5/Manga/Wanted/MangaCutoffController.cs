using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga;
using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http;
using Sonarr.Http.Extensions;

namespace Sonarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure). See DIVERGENCE.md.
    // Role-match analog (filing + plain-Controller shape): src/Sonarr.Api.V5/Manga/Wanted/MissingChaptersController.cs (Plan 06-09 sibling).
    // TV peer reference (cutoff-endpoint semantics): src/Sonarr.Api.V5/Wanted/CutoffController.cs:15-63
    // (extends EpisodeControllerWithSignalR; the manga peer extends plain Controller because manga
    // V5 Wanted controllers do NOT broadcast resource changes from these endpoints — same plain-Controller
    // shape MissingChaptersController uses).
    //
    // Trigger: F-CUTOFF surfaced by Plan 12-99 Task 7 Playwright walkthrough on 2026-05-06 — the
    // frontend route /manga/wanted/cutoffunmet (shipped by Plan 12-08) renders without errors but
    // every fetch to GET /api/v5/manga/wanted/cutoff returned 404 because this controller did not
    // exist. Plan 12-12 ships it in-phase per the no-open-deferrals-at-phase-close standing policy.
    //
    // Backend service-layer dependency: IChapterCutoffService.ChaptersWhereCutoffUnmet
    // (src/NzbDrone.Core/Manga/ChapterCutoffService.cs:10-104 — Phase 8 audit no-sibling/EpisodeCutoffService.md).
    // The service computes belowCutoff translation + custom-format profile id sets, then delegates
    // the paged query to ChapterRepository.ChaptersWhereCutoffUnmet (src/NzbDrone.Core/Manga/ChapterRepository.cs:88-90).
    //
    // Manga sibling preserves (from MissingChaptersController): [V5ApiController] route attribute
    // (literal string per Plan 07-02 URL-shaped key contract); PagingRequestResource shape;
    // monitored filter; FilterExpressions chain; sort keys (releaseDate, chapterNumber, title);
    // optional MangaSubresource lazy hydration via includeManga query.
    //
    // Manga sibling diverges from MissingChaptersController:
    //   * Inject IChapterCutoffService (not IChapterService) — different paged query.
    //   * Drop the languages[] + ageRating filters (cutoff is profile-driven; languages + ageRating
    //     are upstream of the cutoff calculation per ChapterCutoffService:63-103). Keep monitored +
    //     mangaIds[] + includeManga.
    //   * Drop the post-paged in-memory ageRating filter (no ageRating query in this endpoint).
    //
    // Manga sibling diverges from TV's CutoffController:
    //   * Extends plain Controller (not EpisodeControllerWithSignalR) — same plain-Controller shape
    //     MissingChaptersController uses; manga V5 Wanted controllers do not broadcast.
    //   * No CutoffSubresource enum (TV's CutoffController accepts includeSubresources to lazy-hydrate
    //     Series/EpisodeFile/Images on the EpisodeResource); the manga peer surfaces a fixed
    //     MangaCutoffResource shape with optional MangaSubresource via the includeManga bool.
    //
    // Phase 8 cleanup: collapse with TV's CutoffController (rename + flatten to src/Sonarr.Api.V5/Wanted/CutoffController.cs
    // post-Tv-namespace-delete) when Tv/ deletes.
    [V5ApiController("manga/wanted/cutoff")]
    public class MangaCutoffController : Controller
    {
        private readonly IChapterCutoffService _chapterCutoffService;
        private readonly IMangaService _mangaService;

        public MangaCutoffController(IChapterCutoffService chapterCutoffService,
                                     IMangaService mangaService)
        {
            _chapterCutoffService = chapterCutoffService;
            _mangaService = mangaService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<MangaCutoffResource>> GetCutoffUnmetChapters([FromQuery] PagingRequestResource paging,
                                                                              bool monitored = true,
                                                                              [FromQuery] int[]? mangaIds = null,
                                                                              [FromQuery] bool includeManga = false)
        {
            var pagingResource = new PagingResource<MangaCutoffResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<MangaCutoffResource, NzbDrone.Core.Manga.Chapter>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "releaseDate",
                    "chapterNumber",
                    "title"
                },
                "releaseDate",
                SortDirection.Ascending);

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(c => c.Monitored == true);
            }

            if (mangaIds != null && mangaIds.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => mangaIds.Contains(c.MangaId));
            }

            // D-04: NO IsSynthetic filter (mirrors MissingChaptersController). Synthetic rows
            // surface alongside real-feed rows by default. Indexer match upgrades the synthetic
            // row in place per Phase 2 D-17.

            var page = pagingSpec.ApplyToPage(
                spec => _chapterCutoffService.ChaptersWhereCutoffUnmet(spec),
                c => MapToResource(c, includeManga));

            return TypedResults.Ok(page);
        }

        private MangaCutoffResource MapToResource(NzbDrone.Core.Manga.Chapter chapter, bool includeManga)
        {
            var resource = chapter.ToCutoffResource()!;

            if (includeManga)
            {
                var manga = _mangaService.GetManga(chapter.MangaId);
                if (manga != null)
                {
                    resource.Manga = new MangaSubresource
                    {
                        Id = manga.Id,
                        Title = manga.Title
                    };
                }
            }

            return resource;
        }
    }
}
