using Mangarr.Http;
using Mangarr.Http.REST;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;

namespace Mangarr.Api.V5.ManualImport;

// Phase 25 Plan 25-02 — V5 ManualImportController port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 25-01-PORT-SOURCE.md).
//
// V5 manual-import surface — folder-rooted preview + reprocess. Backs the
// /add/import page (25-03) + Wanted/Missing modal (D-03). Mirrors Sonarr V3
// shape verbatim except for the dropped seasonNumber query param (DOMAIN-02 —
// manga has no season concept) and dropped library-rooted overload (Phase 8
// audit gap-04 deferred to v1.2).
//
// Route: [V5ApiController] (bare, no arg) → token-substitution yields
// `/api/v5/manualimport` per D-01. NO namespaced form ('manga/manualimport'
// rejected per D-01 — matches Sonarr V3 path shape verbatim).
//
// Verbs: GET (folder-rooted preview) + POST (reprocess). NO PUT verb. ROADMAP
// §Phase 25 success-criteria currently mentions PUT (line 1293) — this is
// conflated with POST per RESEARCH.md §Summary. 25-05 close-out amends the
// ROADMAP wording. DO NOT author a PUT endpoint here.
//
// Auth: inherits host-pipeline auth filter from [V5ApiController] (X-Api-Key
// header or cookie auth via Mangarr.Http middleware). NO additional [Authorize].
//
// Validation: NO SharedValidator rules — folder-rooted preview accepts empty
// folder per existing service contract (returns []). Per-row error surfacing
// happens inside ManualImportResource.Rejections.
[V5ApiController]
public class ManualImportController : RestController<ManualImportResource>
{
    private readonly IManualImportService _manualImportService;

    public ManualImportController(IManualImportService manualImportService)
    {
        _manualImportService = manualImportService;
    }

    protected override ManualImportResource GetResourceById(int id)
    {
        throw new NotImplementedException("ManualImport rows are ephemeral; not GET-by-id");
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<ManualImportResource>> GetMediaFiles(
        [FromQuery] string folder,
        [FromQuery] string downloadId,
        [FromQuery] int? mangaId,
        [FromQuery] bool filterExistingFiles = true)
    {
        // NO library-rooted (mangaId, volumeNumber?) overload — Phase 8 audit
        // gap-04 deferred to v1.2 per 25-01-PORT-SOURCE.md §3. Folder-rooted only.
        return TypedResults.Ok(
            _manualImportService.GetMediaFiles(folder, downloadId, mangaId, filterExistingFiles).ToResource());
    }

    // ROADMAP amendment lands in 25-05 §success-criteria (PUT mention conflated with POST).
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<List<ManualImportResource>> ReprocessItems([FromBody] List<ManualImportReprocessResource> items)
    {
        return TypedResults.Ok(
            _manualImportService.ReprocessItems(items.ToModel()).ToResource());
    }
}
