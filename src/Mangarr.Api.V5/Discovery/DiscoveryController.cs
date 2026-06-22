using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.Messaging.Commands;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 Plan 42-04 (DISC-02 / DISC-03 / DISC-07) — the REST surface the Discovery
// browse tab + count-only bulk-add modal target. Thin wrapper over IDiscoveryService
// (42-02, the eligibility auto-paging loop) + IManageCommandQueue (enqueues the
// DiscoveryBulkAddCommand from 42-03).
//
// Anti-pattern alert (42-PATTERNS §Pitfall 7): this controller MUST extend the bare
// Controller base — NOT a CRUD-resource base nor a live-push base. Discovery has no
// live entity to broadcast; the add fan-out is handled by MangaController's existing
// IHandle<MangaImportedEvent> subscriber. Broadcasting a live push from here would
// double-fire (the grep guard asserts zero live-push base mentions in this file).
//
// All four endpoints carry [V5ApiController] → the admin X-Api-Key requirement
// (T-42-04-AUTHN, T-AUTHN-01 parity with MangaController). No anonymous browse/bulk-add.
[V5ApiController("discovery")]
public class DiscoveryController : Controller
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly DiscoverySearchRequestValidator _searchRequestValidator;

    public DiscoveryController(IDiscoveryService discoveryService,
                              IManageCommandQueue commandQueueManager,
                              DiscoverySearchRequestValidator searchRequestValidator)
    {
        _discoveryService = discoveryService;
        _commandQueueManager = commandQueueManager;
        _searchRequestValidator = searchRequestValidator;
    }

    [HttpGet("genres")]
    public List<DiscoveryGenreResource> GetGenres()
    {
        return _discoveryService.GetGenres().Select(g => g.ToResource()).ToList();
    }

    [HttpGet("tags")]
    public List<DiscoveryTagResource> GetTags()
    {
        return _discoveryService.GetTags().Select(t => t.ToResource()).ToList();
    }

    [HttpPost("search")]
    public Results<Ok<DiscoverySearchResponseResource>, BadRequest> Search([FromBody] DiscoverySearchRequestResource resource)
    {
        // T-42-04-TAMPER: clamp X + whitelist enums + bound ranges BEFORE the filter
        // reaches MangaBakaApi.Browse. 400 on any violation.
        var validationResult = _searchRequestValidator.Validate(resource);

        if (!validationResult.IsValid)
        {
            return TypedResults.BadRequest();
        }

        var result = _discoveryService.Search(resource.ToFilter(), resource.X);

        return TypedResults.Ok(result.ToResource());
    }

    [HttpPost("bulk-add")]
    public Accepted BulkAdd([FromBody] DiscoveryBulkAddResource resource)
    {
        // T-42-04-DOS: clamp the id list to 100 (the grid's max X) before enqueue; the
        // downstream refresh fan-out self-paces via the MangaBaka LookupRateLimit.
        _commandQueueManager.Push(new DiscoveryBulkAddCommand
        {
            MangaBakaIds = (resource.MangaBakaIds ?? []).Take(100).ToList(),
            RootFolderPath = resource.RootFolderPath,
            Monitor = resource.Monitor,
            TranslationProfileId = resource.TranslationProfileId,
            CustomFormatProfileId = resource.CustomFormatProfileId,
            Tags = resource.Tags ?? [],
            SearchForMissingChapters = resource.SearchForMissingChapters
        });

        // Fire-and-forget (D-07) — 202 immediately; the command queue reports progress.
        return TypedResults.Accepted((string?)null);
    }
}
