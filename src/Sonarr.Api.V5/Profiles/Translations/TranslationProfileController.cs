using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Profiles.Translations;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Profiles.Translations;

// Sonarr divergence: NEW V5 controller per Phase 5 D-01 — see DIVERGENCE.md.
// /api/v5/translationprofile CRUD. Mirrors QualityProfileController shape verbatim.
// FluentValidation rules enforce ASVS L1 V5 Input Validation per Phase 5 RESEARCH §Security Domain:
//   - Name: NotEmpty (FluentValidation framework)
//   - Languages: NotEmpty + Count <= 50 (DoS cap on unbounded JSON-column write)
//   - Languages each entry: BCP-47 valid (validated via IsoLanguages.Find — non-null lookup result
//     in NzbDrone.Core.Parser.IsoLanguages is the closest existing API; IsBcp47Valid does NOT exist
//     in this codebase. Find() handles 2-letter, 3-letter, and 2-letter-COUNTRY (e.g. pt-br) shapes.
//     Documented in 05-02-SUMMARY.md.)
[V5ApiController]
public class TranslationProfileController : RestController<TranslationProfileResource>
{
    private readonly ITranslationProfileService _profileService;

    public TranslationProfileController(ITranslationProfileService profileService)
    {
        _profileService = profileService;

        // WR-04: cap Name length (mirrors MangaNamingConfigController precedent) — NotEmpty alone
        // would let a client submit a 10MB Name and pollute the SQLite TranslationProfiles.Name
        // column.
        SharedValidator.RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        SharedValidator.RuleFor(c => c.Languages).NotEmpty()
            .Must(l => l != null && l.Count <= 50 && l.All(code => code != null && code.Length <= 32))
            .WithMessage("Languages list must contain at most 50 entries and each code at most 32 chars (security cap per Phase 5 RESEARCH §Security Domain).");
        SharedValidator.RuleFor(c => c.Languages)
            .Must(langs => langs == null || langs.All(code => !string.IsNullOrWhiteSpace(code) && IsoLanguages.Find(code) != null))
            .WithMessage("All language entries must be valid BCP-47 codes (e.g., en, es, ja, pt-br).");
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<TranslationProfileResource>, NotFound> Create([FromBody] TranslationProfileResource resource)
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
    public Results<Accepted<TranslationProfileResource>, NotFound> Update([FromBody] TranslationProfileResource resource)
    {
        var model = resource.ToModel();
        _profileService.Update(model);
        return TypedAccepted(model.Id);
    }

    protected override TranslationProfileResource GetResourceById(int id)
    {
        return _profileService.Get(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<TranslationProfileResource>> GetAll()
    {
        return TypedResults.Ok(_profileService.All().ToResource());
    }
}
