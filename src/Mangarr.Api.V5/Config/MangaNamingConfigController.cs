using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Organizer.Manga;

namespace Mangarr.Api.V5.Config;

// Sonarr divergence: NEW V5 controller per Phase 5 D-13 + Open Question 4 — see DIVERGENCE.md.
// V5 sibling to V3 NamingConfigController. Exposes manga-shaped subset of the NamingConfig
// singleton + a GET /presets/manga endpoint that returns the locked reader-compat preset list.
// FluentValidation rules per Phase 5 RESEARCH §Security Domain (ASVS L1 V5 Input Validation):
//   - StandardChapterFormat: NotEmpty + MaximumLength(200) — DoS cap on user template
//   - MangaFolderFormat:     NotEmpty + MaximumLength(200) — DoS cap on user template
// Pitfall 9 mitigation: Save() goes through INamingConfigService (standard Upsert + cache
// invalidation) rather than touching the SQLite row directly.
[V5ApiController("config/manganaming")]
public class MangaNamingConfigController : RestController<MangaNamingConfigResource>
{
    private readonly INamingConfigService _namingConfigService;

    public MangaNamingConfigController(INamingConfigService namingConfigService)
    {
        _namingConfigService = namingConfigService;

        SharedValidator.RuleFor(c => c.StandardChapterFormat).NotEmpty().MaximumLength(200);
        SharedValidator.RuleFor(c => c.MangaFolderFormat).NotEmpty().MaximumLength(200);
    }

    protected override MangaNamingConfigResource GetResourceById(int id)
    {
        return _namingConfigService.GetConfig().ToMangaResource();
    }

    [HttpGet]
    public Ok<MangaNamingConfigResource> GetMangaNamingConfig()
    {
        return TypedResults.Ok(_namingConfigService.GetConfig().ToMangaResource());
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<MangaNamingConfigResource>, NotFound> Update([FromBody] MangaNamingConfigResource resource)
    {
        var current = _namingConfigService.GetConfig();
        current.ApplyMangaFields(resource);
        _namingConfigService.Save(current);
        return TypedAccepted(current.Id);
    }

    [HttpGet("presets/manga")]
    public Ok<List<MangaNamingPreset>> GetMangaPresets()
    {
        return TypedResults.Ok(MangaNamingPresets.All.ToList());
    }
}
