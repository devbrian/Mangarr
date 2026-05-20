using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.Extensions;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.Exclusions;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — ported from
// .planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ImportListExclusionController.cs
// with the Sonarr `TvdbId` int-keyed uniqueness rule swapped for Mangarr's
// `MangaDexId` string-keyed rule (Migration 003 manga-ID triplet shape).
//
// Allowed sort keys for the paged GET endpoint (used by FE TablePager): id, title,
// mangaDexId. The Sonarr ref slice also exposed tvdbId — replaced by mangaDexId
// here per the triplet swap.
[V5ApiController]
public class ImportListExclusionController : RestController<ImportListExclusionResource>
{
    private readonly IImportListExclusionService _importListExclusionService;

    public ImportListExclusionController(IImportListExclusionService importListExclusionService,
                                         ImportListExclusionExistsValidator importListExclusionExistsValidator)
    {
        _importListExclusionService = importListExclusionService;

        // Sonarr-canonical Cascade.Stop: short-circuit if the value is missing
        // before running the existence/uniqueness check.
        SharedValidator.RuleFor(c => c.MangaDexId).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .SetValidator(importListExclusionExistsValidator);

        SharedValidator.RuleFor(c => c.Title).NotEmpty();
    }

    protected override ImportListExclusionResource GetResourceById(int id)
    {
        return _importListExclusionService.Get(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<PagingResource<ImportListExclusionResource>> GetImportListExclusions([FromQuery] PagingRequestResource paging)
    {
        var pagingResource = new PagingResource<ImportListExclusionResource>(paging);
        var pageSpec = pagingResource.MapToPagingSpec<ImportListExclusionResource, ImportListExclusion>(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "id",
                "title",
                "mangaDexId"
            },
            "id",
            SortDirection.Descending);

        return TypedResults.Ok(pageSpec.ApplyToPage(_importListExclusionService.Paged, ImportListExclusionResourceMapper.ToResource));
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<ImportListExclusionResource>, NotFound> AddImportListExclusion([FromBody] ImportListExclusionResource resource)
    {
        var importListExclusion = _importListExclusionService.Add(resource.ToModel());

        return TypedCreated(importListExclusion.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<ImportListExclusionResource>, NotFound> UpdateImportListExclusion([FromBody] ImportListExclusionResource resource)
    {
        _importListExclusionService.Update(resource.ToModel());

        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteImportListExclusion(int id)
    {
        _importListExclusionService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Consumes("application/json")]
    public NoContent DeleteImportListExclusions([FromBody] ImportListExclusionBulkResource resource)
    {
        _importListExclusionService.Delete(resource.Ids.ToList());

        return TypedResults.NoContent();
    }
}
