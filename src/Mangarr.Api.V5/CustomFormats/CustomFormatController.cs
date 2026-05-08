using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Validation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;

namespace Mangarr.Api.V5.CustomFormats;

// Sonarr divergence: NEW V5 port per Phase 5 D-10 + Open Question 3 — see DIVERGENCE.md.
// Mirrors Sonarr.Api.V3.CustomFormats.CustomFormatController shape verbatim with two
// changes: (1) V5 attribute + namespace; (2) `?mediaType=manga` query filter on the
// schema endpoint per Phase 5 RESEARCH §Open Question 3 (chosen over a separate endpoint
// to avoid duplicating the schema response shape).
//
// CF-04 round-trip (export/import the 4 NEW manga spec types lossless) is INHERITED
// via CustomFormatResource.MapSpecification reflection-by-name (CustomFormatResource.cs:55)
// — the manga specs auto-flow because they implement ICustomFormatSpecification. Wave 4
// plan 05-07's MangaCustomFormatRoundTripFixture verifies end-to-end.
//
// Phase 8 cleanup: drop the `?mediaType` filter when only manga remains (Tv/ deletes).
[V5ApiController]
public class CustomFormatController : RestController<CustomFormatResource>
{
    private readonly ICustomFormatService _formatService;
    private readonly List<ICustomFormatSpecification> _specifications;

    public CustomFormatController(ICustomFormatService formatService,
                                  List<ICustomFormatSpecification> specifications)
    {
        _formatService = formatService;
        _specifications = specifications;

        SharedValidator.RuleFor(c => c.Name).NotEmpty();
        SharedValidator.RuleFor(c => c.Name)
            .Must((v, c) => !_formatService.All().Any(f => f.Name == c && f.Id != v.Id)).WithMessage("Must be unique.");
        SharedValidator.RuleFor(c => c.Specifications).NotEmpty();
        SharedValidator.RuleFor(c => c).Custom((customFormat, context) =>
        {
            if (customFormat.Specifications == null || !customFormat.Specifications.Any())
            {
                context.AddFailure("Must contain at least one Condition");
                return;
            }

            if (customFormat.Specifications.Any(s => s.Name.IsNullOrWhiteSpace()))
            {
                context.AddFailure("Condition name(s) cannot be empty or consist of only spaces");
            }
        });
    }

    protected override CustomFormatResource GetResourceById(int id)
    {
        return _formatService.GetById(id).ToResource(true);
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<CustomFormatResource>> GetAll()
    {
        return TypedResults.Ok(_formatService.All().ToResource(true));
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<CustomFormatResource>, NotFound> Create([FromBody] CustomFormatResource customFormatResource)
    {
        var model = customFormatResource.ToModel(_specifications);

        Validate(model);

        return TypedCreated(_formatService.Insert(model).Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<CustomFormatResource>, NotFound> Update([FromBody] CustomFormatResource resource)
    {
        var model = resource.ToModel(_specifications);

        Validate(model);

        _formatService.Update(model);

        return TypedAccepted(model.Id);
    }

    [HttpPut("bulk")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public virtual ActionResult<List<CustomFormatResource>> UpdateBulk([FromBody] CustomFormatBulkResource resource)
    {
        if (!resource.Ids.Any())
        {
            throw new BadRequestException("ids must be provided");
        }

        var customFormats = resource.Ids.Select(id => _formatService.GetById(id)).ToList();

        customFormats.ForEach(existing =>
        {
            existing.IncludeCustomFormatWhenRenaming = resource.IncludeCustomFormatWhenRenaming ?? existing.IncludeCustomFormatWhenRenaming;
        });

        _formatService.Update(customFormats);

        return Accepted(customFormats.ConvertAll(cf => cf.ToResource(true)));
    }

    [RestDeleteById]
    public NoContent DeleteFormat(int id)
    {
        _formatService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Consumes("application/json")]
    public virtual object DeleteFormats([FromBody] CustomFormatBulkResource resource)
    {
        _formatService.Delete(resource.Ids.ToList());

        return new { };
    }

    [HttpGet("schema")]
    public object GetTemplates([FromQuery] string? mediaType = null)
    {
        // Phase 5 D-10 — filter specs by AppliesTo discriminator per Open Question 3.
        // Phase 8 cleanup: drop the filter (only manga remains after Tv/ deletes).
        IEnumerable<ICustomFormatSpecification> filteredSpecs = _specifications;

        if (string.Equals(mediaType, "manga", StringComparison.OrdinalIgnoreCase))
        {
            filteredSpecs = filteredSpecs.Where(s => s.AppliesTo == MediaType.Manga || s.AppliesTo == MediaType.All);
        }
        else if (mediaType == null || string.Equals(mediaType, "series", StringComparison.OrdinalIgnoreCase))
        {
            filteredSpecs = filteredSpecs.Where(s => s.AppliesTo == MediaType.Series || s.AppliesTo == MediaType.All);
        }

        // Unknown / future mediaType values fall through with no filter (safe default for v2 expansion).
        var schema = filteredSpecs.OrderBy(x => x.Order).Select(x => x.ToSchema()).ToList();

        var presets = GetPresets().ToList();

        foreach (var item in schema)
        {
            item.Presets = presets.Where(x => x.GetType().Name == item.Implementation).Select(x => x.ToSchema()).ToList();
        }

        return schema;
    }

    private void Validate(CustomFormat definition)
    {
        foreach (var validationResult in definition.Specifications.Select(spec => spec.Validate()))
        {
            VerifyValidationResult(validationResult);
        }
    }

    private void VerifyValidationResult(ValidationResult validationResult)
    {
        var result = new NzbDroneValidationResult(validationResult.Errors);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    private IEnumerable<ICustomFormatSpecification> GetPresets()
    {
        yield return new ReleaseTitleSpecification
        {
            Name = "x264",
            Value = @"(x|h)\.?264"
        };

        yield return new ReleaseTitleSpecification
        {
            Name = "x265",
            Value = @"(((x|h)\.?265)|(HEVC))"
        };

        yield return new ReleaseTitleSpecification
        {
            Name = "Simple Hardcoded Subs",
            Value = @"subs?"
        };

        yield return new ReleaseTitleSpecification
        {
            Name = "Hardcoded Subs",
            Value = @"\b(?<hcsub>(\w+SUBS?)\b)|(?<hc>(HC|SUBBED))\b"
        };

        yield return new ReleaseTitleSpecification
        {
            Name = "Surround Sound",
            Value = @"DTS.?(HD|ES|X(?!\D))|TRUEHD|ATMOS|DD(\+|P).?([5-9])|EAC3.?([5-9])"
        };

        yield return new ReleaseTitleSpecification
        {
            Name = "Preferred Words",
            Value = @"\b(SPARKS|Framestor)\b"
        };

        var formats = _formatService.All();
        foreach (var format in formats)
        {
            foreach (var condition in format.Specifications)
            {
                var preset = condition.Clone();
                preset.Name = $"{format.Name}: {preset.Name}";
                yield return preset;
            }
        }
    }
}

// V5-internal sibling of Sonarr.Api.V3.CustomFormats.CustomFormatBulkResource — same shape.
public class CustomFormatBulkResource
{
    public HashSet<int> Ids { get; set; } = new();
    public bool? IncludeCustomFormatWhenRenaming { get; set; }
}
