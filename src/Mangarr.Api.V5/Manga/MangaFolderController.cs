using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Organizer.Manga;

namespace Mangarr.Api.V5.Manga;

// Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-05 — see DIVERGENCE.md.
// Role-match analog: src/Mangarr.Api.V5/Series/SeriesFolderController.cs:11-34
// Preserves: bare Controller base, [HttpGet("{id}/folder")] action template, shared route
//   prefix with MangaController (RESEARCH §Pitfall 2 — route attribute MUST be "manga" not
//   "manga/folder"; the action template owns the /folder suffix).
// Diverges: ISeriesService → IMangaService; IBuildFileNames → IBuildMangaFileNames; series
//   namespace → manga namespace; GetSeriesFolder(series) → GetMangaFolder(manga, null) (manga
//   peer takes optional NamingConfig second arg per IBuildMangaFileNames.cs:33).
// Phase 15 collapse: SeriesFolderController deletion + namespace rename will collapse this
//   peer back into a single FolderController per the unified domain.
//
// Forward-prophylactic per D-13-04: frontend RootFolderModalContent.tsx:39 currently calls
// /series/{seriesId}/folder; this controller is the manga peer needed for v1 regardless of
// current frontend wiring (Phase 15 cutover concern).

[V5ApiController("manga")]
public class MangaFolderController : Controller
{
    private readonly IMangaService _mangaService;
    private readonly IBuildMangaFileNames _fileNameBuilder;

    public MangaFolderController(IMangaService mangaService, IBuildMangaFileNames fileNameBuilder)
    {
        _mangaService = mangaService;
        _fileNameBuilder = fileNameBuilder;
    }

    [HttpGet("{id}/folder")]
    [Produces("application/json")]
    public Ok<object> GetFolder([FromRoute] int id)
    {
        var manga = _mangaService.GetManga(id);
        var folder = _fileNameBuilder.GetMangaFolder(manga, null);

        return TypedResults.Ok((object)new { folder });
    }
}
