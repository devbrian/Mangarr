using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Manga;
using Sonarr.Http;

namespace Sonarr.Api.V5.Manga;

// Phase 2 manual cross-source relink endpoint per D-23 + Plan 02-10. Per D-23: this is
// the "manual-only relink" surface invoked AFTER add-time auto-resolve has already run.
// CrossSourceIdResolver is BYPASSED here — user-supplied IDs are accepted verbatim
// (no Jaro-Winkler, no multi-axis confirm). Auto-validation only runs at add-time and
// on refresh; this endpoint is the explicit user override.
//
// Threat T-INJ-04 mitigation: body fields are typed (Guid?, int?), so no string
// concatenation or path injection is possible. Repository writes go through Dapper
// parameterized queries.
[V5ApiController("manga")]
public class MangaLinksController : Controller
{
    private readonly IMangaService _mangaService;

    public MangaLinksController(IMangaService mangaService)
    {
        _mangaService = mangaService;
    }

    [HttpPost("{id}/links")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Ok<MangaResource>, NotFound> UpdateLinks(int id, [FromBody] LinkUpdateRequest body)
    {
        var manga = _mangaService.GetManga(id);

        if (manga == null)
        {
            return TypedResults.NotFound();
        }

        // PER D-23: manual-only relink. No auto-validation against secondary sources;
        // user explicitly sets the IDs. CrossSourceIdResolver is BYPASSED here.
        if (body.MangaDexId.HasValue)
        {
            manga.MangaDexId = body.MangaDexId;
        }

        if (body.MalId.HasValue)
        {
            manga.MalId = body.MalId;
        }

        if (body.AniListId.HasValue)
        {
            manga.AniListId = body.AniListId;
        }

        _mangaService.UpdateManga(manga);

        var resource = manga.ToResource();

        return resource == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(resource);
    }

    public class LinkUpdateRequest
    {
        public Guid? MangaDexId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }
    }
}
