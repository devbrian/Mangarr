using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;

namespace Mangarr.Api.V5.Manga;

// META-01 developer-surface lookup endpoint per Plan 02-10. Mirrors Mangarr's
// SeriesLookupController shape but dispatches via IMetadataSourceFactory.GetPrimary()
// instead of a single concrete proxy (D-15 dynamic primary). [V5ApiController]
// attribute carries the admin X-Api-Key requirement per RESEARCH §Security Domain V4.
[V5ApiController("manga/lookup")]
public class MangaLookupController : Controller
{
    private readonly IMetadataSourceFactory _metaFactory;
    private readonly IMapMangaCoversToLocal _coverMapper;

    public MangaLookupController(IMetadataSourceFactory metaFactory,
                                 IMapMangaCoversToLocal coverMapper)
    {
        _metaFactory = metaFactory;
        _coverMapper = coverMapper;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<IEnumerable<MangaResource>> Search([FromQuery] string term)
    {
        // D-15: resolve primary at REQUEST TIME — user can change primary without restart.
        var primaryDef = _metaFactory.GetPrimary();
        var primary = _metaFactory.GetInstance(primaryDef);

        // UUID short-circuit: if the user pastes a MangaDex UUID into the search input,
        // route to the by-id lookup instead of fuzzy title search (which won't match a UUID).
        // The provider returns an empty list on 404 so an unknown UUID falls back to the
        // existing no-match UX (zero results). Mirrors the Sonarr-shape "paste TVDB id" UX.
        var hits = Guid.TryParse(term, out _)
            ? primary.SearchForNewMangaByMangaDexId(term)
            : primary.SearchForNewManga(term);

        return TypedResults.Ok<IEnumerable<MangaResource>>(MapAll(hits));
    }

    private IEnumerable<MangaResource> MapAll(List<NzbDrone.Core.Manga.Manga> hits)
    {
        foreach (var manga in hits)
        {
            var resource = manga.ToResource();

            if (resource == null)
            {
                continue;
            }

            // mangaId=0 — these results are not yet persisted; the cover mapper still
            // registers remote URLs through the proxy so the frontend can fetch them.
            if (resource.Images != null)
            {
                _coverMapper.ConvertToLocalUrls(0, resource.Images);
            }

            yield return resource;
        }
    }
}
