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
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Wanted/MissingController.cs.
    //
    // WANTED-01..03: paged list of monitored chapters that have no ChapterFile imported,
    // filterable by mangaIds + languages + ageRating.
    //
    // Manga sibling preserves: [V5ApiController] route attribute; PagingRequestResource shape;
    // monitored filter; FilterExpressions chain.
    //
    // Manga sibling diverges from MissingController:
    //   * Inject IChapterService (paged ChaptersWithoutFiles overload added by Plan 06-09)
    //     + IMangaService (for ageRating filter join + includeManga hydration).
    //   * Drop EpisodesWithoutFiles + includeSpecials (manga has no specials concept).
    //   * Add mangaIds + languages + ageRating filters per WANTED-03.
    //   * D-04 honored: IsSynthetic=true rows are surfaced identically to IsSynthetic=false
    //     rows by default. No `WHERE IsSynthetic = false` filter is applied. A future
    //     `excludeSynthetic` query param could opt-in to the filter; v1 default is INCLUDE.
    //   * ageRating filter is post-paged (in-memory) because it requires a JOIN to Manga
    //     and the v1 ChapterRepository.ChaptersWithoutFiles does not JOIN. Acceptable for
    //     Phase 6 — the result set is bounded by the paging spec (default 10/page).
    //
    // Phase 8 cleanup: collapse with MissingController when Tv/ deletes.
    [V5ApiController("manga/wanted/missing")]
    public class MissingChaptersController : Controller
    {
        private readonly IChapterService _chapterService;
        private readonly IMangaService _mangaService;

        public MissingChaptersController(IChapterService chapterService,
                                         IMangaService mangaService)
        {
            _chapterService = chapterService;
            _mangaService = mangaService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<MissingChapterResource>> GetMissingChapters([FromQuery] PagingRequestResource paging,
                                                                            bool monitored = true,
                                                                            [FromQuery] int[]? mangaIds = null,
                                                                            [FromQuery] string[]? languages = null,
                                                                            [FromQuery] string? ageRating = null,
                                                                            [FromQuery] bool includeManga = false)
        {
            var pagingResource = new PagingResource<MissingChapterResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<MissingChapterResource, Chapter>(
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

            if (languages != null && languages.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => languages.Contains(c.TranslatedLanguage));
            }

            // D-04: NO IsSynthetic filter. Synthetic rows are placeholder rows from the
            // metadata-only-count fallback (Phase 2 D-17) and surface identically to real-feed
            // rows in the Wanted list. Indexer match upgrades the synthetic row in place per
            // Phase 2 D-17.

            // ageRating requires a Manga JOIN; apply post-paged in-memory.
            // The result set is bounded by the paging spec (default 10/page) so the
            // in-memory pass cost is negligible. Future Phase 8 collapse may push the
            // filter into the SQL builder.
            var page = pagingSpec.ApplyToPage(
                spec => _chapterService.ChaptersWithoutFiles(spec),
                c => MapToResource(c, includeManga));

            if (!string.IsNullOrWhiteSpace(ageRating))
            {
                var mangaIdsInPage = page.Records.Select(r => r.MangaId).Distinct().ToList();
                var mangaLookup = _mangaService.GetManga(mangaIdsInPage)
                    .ToDictionary(m => m.Id, m => m.ContentRating);
                page.Records = page.Records
                    .Where(r => mangaLookup.TryGetValue(r.MangaId, out var rating)
                                && string.Equals(rating, ageRating, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return TypedResults.Ok(page);
        }

        private MissingChapterResource MapToResource(Chapter chapter, bool includeManga)
        {
            var resource = chapter.ToMissingResource()!;

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
