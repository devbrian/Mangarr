using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.CustomFormats;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Profiles.CustomFormats;

// Sonarr divergence: NEW V5 controller per Phase 5 D-07 — see DIVERGENCE.md.
// /api/v5/customformatprofile CRUD. Mirrors QualityProfileController shape verbatim.
// V5 Input Validation per ASVS L1:
//   - Name: NotEmpty
//   - MinFormatScore: int (FluentValidation type-check at framework level)
//   - MaxFormatScore: int? optional but if provided must be >= MinFormatScore
[V5ApiController]
public class CustomFormatProfileController : RestController<CustomFormatProfileResource>
{
    private readonly ICustomFormatProfileService _profileService;

    public CustomFormatProfileController(ICustomFormatProfileService profileService)
    {
        _profileService = profileService;

        // WR-04: cap Name length + cap FormatItems count (DoS surface). NotEmpty alone would
        // let a client submit a 10MB Name and pollute the SQLite CustomFormatProfiles.Name
        // column; the Default profile pre-populates one FormatItems entry per existing CF, so
        // a client could push thousands of orphan entries without an upper bound.
        SharedValidator.RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        SharedValidator.RuleFor(c => c.FormatItems)
            .Must(items => items == null || items.Count <= 1000)
            .WithMessage("FormatItems list must contain at most 1000 entries (security cap per Phase 5 RESEARCH §Security Domain).");
        SharedValidator.RuleFor(c => c.MaxFormatScore)
            .GreaterThanOrEqualTo(c => c.MinFormatScore)
            .When(c => c.MaxFormatScore.HasValue)
            .WithMessage("MaxFormatScore must be greater than or equal to MinFormatScore.");
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<CustomFormatProfileResource>, NotFound> Create([FromBody] CustomFormatProfileResource resource)
    {
        var model = resource.ToModel();
        model = _profileService.Add(model);
        return TypedCreated(model.Id);
    }

    [RestDeleteById]
    public NoContent DeleteProfile(int id)
    {
        _profileService.Delete(id);
        return TypedResults.NoContent();
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<CustomFormatProfileResource>, NotFound> Update([FromBody] CustomFormatProfileResource resource)
    {
        var model = resource.ToModel();
        _profileService.Update(model);
        return TypedAccepted(model.Id);
    }

    protected override CustomFormatProfileResource GetResourceById(int id)
    {
        return _profileService.Get(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<CustomFormatProfileResource>> GetAll()
    {
        return TypedResults.Ok(_profileService.All().ToResource());
    }
}
