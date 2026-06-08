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
        // WR-02 (18-REVIEW): empty/whitespace term must short-circuit BEFORE
        // we ask any provider — SearchForNewManga("") behavior is provider-
        // dependent and the new UUID-vs-title branch widens that surface.
        if (string.IsNullOrWhiteSpace(term))
        {
            return TypedResults.Ok<IEnumerable<MangaResource>>(Enumerable.Empty<MangaResource>());
        }

        // D-15: resolve primary at REQUEST TIME — user can change primary without restart.
        var primaryDef = _metaFactory.GetPrimary();
        var primary = _metaFactory.GetInstance(primaryDef);

        // By-id short-circuit: if the user pastes a MangaDex UUID OR an integer MangaBaka id
        // into the search input, route to the by-id lookup instead of fuzzy title search
        // (which won't match a raw id). The numeric branch serves MangaBaka's integer ids
        // (parallel to the MangaDex UUID branch); for the MangaBaka primary,
        // SearchForNewMangaByMangaDexId performs the int-by-id lookup (param name is
        // legacy-string, behavior is provider-defined per Plan 41-03). ISearchForNewManga is
        // deliberately NOT widened (D-07-R / Open Q1 — 4-provider blast radius).
        // The provider returns an empty list on 404 so an unknown id falls back to the
        // existing no-match UX (zero results). Mirrors the Sonarr-shape "paste TVDB id" UX.
        List<NzbDrone.Core.Manga.Manga> hits;
        if (Guid.TryParse(term, out _) || int.TryParse(term, out _))
        {
            try
            {
                hits = primary.SearchForNewMangaByMangaDexId(term);
            }
            catch (Exception)
            {
                // WR-02 (18-REVIEW): defensive fallback — only MangaDexMetadataSource
                // is contractually known to swallow 404s into an empty list. AniList /
                // MAL / Comix providers may throw on an unrecognized UUID. Fall
                // through to the title search so the user sees the no-match UX
                // instead of a 500.
                hits = primary.SearchForNewManga(term);
            }
        }
        else
        {
            hits = primary.SearchForNewManga(term);
        }

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
