using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga;
using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Manga.Blocklist
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Blocklist/BlocklistController.cs.
    //
    // BLOCK-01..02: paged blocklist listing + single-row delete + bulk delete.
    //
    // Manga sibling preserves: [V5ApiController] route attribute; PagingRequestResource shape;
    // [RestDeleteById] for ID-keyed delete; [HttpDelete("bulk")] for batch delete.
    //
    // Manga sibling diverges from BlocklistController:
    //   * Inject IMangaBlocklistService (Plan 06-04) instead of IBlocklistService.
    //   * Drop ICustomFormatCalculationService (manga has no quality model per Phase 5 D-04;
    //     CF score is computed at decision time, not surfaced on blocklist resource).
    //   * Filter mangaIds (not seriesIds); drop protocols filter (v1 only DownloadProtocol.Http).
    //
    // Phase 8 cleanup: collapse with BlocklistController when Tv/ deletes.
    [V5ApiController("manga/blocklist")]
    public class MangaBlocklistController : Controller
    {
        private readonly IMangaBlocklistService _blocklistService;
        private readonly IMangaService _mangaService;

        public MangaBlocklistController(IMangaBlocklistService blocklistService,
                                        IMangaService mangaService)
        {
            _blocklistService = blocklistService;
            _mangaService = mangaService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<MangaBlocklistResource>> GetBlocklist([FromQuery] PagingRequestResource paging,
                                                                      [FromQuery] int[]? mangaIds = null,
                                                                      [FromQuery] bool includeManga = false)
        {
            var pagingResource = new PagingResource<MangaBlocklistResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<MangaBlocklistResource, MangaBlocklist>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "date",
                    "sourceTitle",
                    "sourceKey"
                },
                "date",
                SortDirection.Descending);

            if (mangaIds != null && mangaIds.Any())
            {
                pagingSpec.FilterExpressions.Add(b => mangaIds.Contains(b.MangaId));
            }

            var resource = pagingSpec.ApplyToPage(
                spec => _blocklistService.Paged(spec),
                b => MapToResource(b, includeManga));

            return TypedResults.Ok(resource);
        }

        [RestDeleteById]
        public NoContent DeleteBlocklist(int id)
        {
            _blocklistService.Delete(id);
            return TypedResults.NoContent();
        }

        [HttpDelete("bulk")]
        [Produces("application/json")]
        public NoContent Remove([FromBody] MangaBlocklistBulkResource resource)
        {
            _blocklistService.Delete(resource.Ids);
            return TypedResults.NoContent();
        }

        private MangaBlocklistResource MapToResource(MangaBlocklist model, bool includeManga)
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

            return resource;
        }
    }
}
