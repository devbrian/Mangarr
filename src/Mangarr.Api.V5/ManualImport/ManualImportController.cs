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
// SECURITY: The `folder` query parameter on GET /api/v5/manualimport accepts
// any filesystem path the host process can read — there is NO restriction to
// configured root folders or the staging directory. This matches Sonarr v3/v5
// TV-side behavior verbatim and the threat model treats authenticated callers
// (X-Api-Key or cookie) as fully trusted with respect to filesystem listing
// over the manga-archive extension allowlist (cbz/cbr/cb7/cbt/zip/rar/pdf/epub).
//
// Explicit decision (Phase 25 code review WR-02 + close-out 2026-05-19):
// "authenticated → trusted" is the documented disposition for v1. The
// fixture `scans_arbitrary_folder` in InteractiveImportFolderFixture asserts
// this contract. If Phase 99 (distribution) introduces multi-user auth, this
// controller MUST gain a root-folder containment validator BEFORE shipping —
// either (a) validate `folder` resolves under IRootFolderService.All() or the
// configured staging dir, OR (b) reaffirm the trust decision and document why.
// See `.planning/phases/25-…/25-REVIEW.md` §WR-02 for the validator sketch.
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
        [FromQuery] string? folder = null,
        [FromQuery] string? downloadId = null,
        [FromQuery] int? mangaId = null,
        [FromQuery] bool filterExistingFiles = true)
    {
        // NO library-rooted (mangaId, volumeNumber?) overload — Phase 8 audit
        // gap-04 deferred to v1.2 per 25-01-PORT-SOURCE.md §3. Folder-rooted only.
        //
        // All query params nullable + default-null per V5 minimal-API binding
        // contract — Phase 25 post-merge L-002 manual smoke caught that non-
        // nullable `string` params without defaults are bound as REQUIRED in
        // .NET 10 minimal-API path, producing HTTP 400 on every page-load
        // (initial-mount with no folder/downloadId AND folder-only scans).
        // ManualImportService.GetMediaFiles already returns [] for null folder
        // and silently skips the downloadId fast-path when null/blank — so
        // making the params optional restores the Sonarr V3 contract verbatim.
        return TypedResults.Ok(
            _manualImportService.GetMediaFiles(folder, downloadId, mangaId, filterExistingFiles).ToResource());
    }

    // ROADMAP amendment lands in 25-05 §success-criteria (PUT mention conflated with POST).
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<List<ManualImportResource>> ReprocessItems([FromBody] List<ManualImportReprocessResource>? items)
    {
        // Null-coalesce per code-review WR-01 — POST with empty/missing body
        // would otherwise NRE on the .ToModel() extension call.
        return TypedResults.Ok(
            _manualImportService.ReprocessItems((items ?? new List<ManualImportReprocessResource>()).ToModel()).ToResource());
    }
}
