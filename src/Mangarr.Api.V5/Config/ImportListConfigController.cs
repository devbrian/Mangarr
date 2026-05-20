using System.Reflection;
using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Mangarr.Http.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;

namespace Mangarr.Api.V5.Config;

// Phase 27.1 Plan 27.1-01 — closes GH #220 backend half.
// Mirrors DownloadClientConfigController.cs shape verbatim (Mangarr-canonical
// V5 Config controller pattern: RestController<TResource> + reflection-based
// SaveConfigDictionary). RESEARCH Pitfall 2 amendment to CONTEXT.md D-06:
// Mangarr has no ConfigController<T> abstraction — use RestController<T> peer.
//
// SharedValidator carries the Sonarr v3 parity rule from
// src/Sonarr.Api.V3/Config/ImportListConfigController.cs:18-21 — ListSyncTag
// must be > 0 when ListSyncLevel == KeepAndTag, otherwise users could save a
// configuration that silently no-ops (or worse: corrupts Manga.Tags with id 0)
// at ImportListSyncService.ProcessListItems evaluation time.
[V5ApiController("config/importlist")]
public class ImportListConfigController : RestController<ImportListConfigResource>
{
    private readonly IConfigService _configService;

    public ImportListConfigController(IConfigService configService)
    {
        _configService = configService;

        SharedValidator.RuleFor(x => x.ListSyncTag)
            .ValidId()
            .WithMessage("Tag must be specified")
            .When(x => x.ListSyncLevel == ListSyncLevelType.KeepAndTag);
    }

    protected override ImportListConfigResource GetResourceById(int id)
    {
        return GetConfig();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<ImportListConfigResource> GetImportListConfig()
    {
        return TypedResults.Ok(GetConfig());
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<ImportListConfigResource>, NotFound> SaveConfig([FromBody] ImportListConfigResource resource)
    {
        var dictionary = resource.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

        _configService.SaveConfigDictionary(dictionary);

        return TypedAccepted(resource.Id);
    }

    private ImportListConfigResource GetConfig()
    {
        var resource = ImportListConfigResourceMapper.ToResource(_configService);
        resource.Id = 1;
        return resource;
    }
}
