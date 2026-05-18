using FluentValidation;
using FluentValidation.Results;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Validation;

namespace Mangarr.Api.V5.AutoTagging;

// Phase 24 Plan 24-04 — V5 AutoTaggingController port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 24-01-PORT-SOURCE.md).
//
// Open Q #3 resolution (24-PLAN-DECISIONS.md §Open Q #3): the schema endpoint is
// INLINED on this controller as [HttpGet("schema")] — there is NO separate
// AutoTaggingSpecificationController.cs file. The Sonarr V5 source at the pinned
// SHA does the same (verified via WebFetch in 24-01-PORT-SOURCE.md Pattern D).
//
// Open Q #4 resolution: route is auto-derived from controller class name via
// [V5ApiController] token substitution -> /api/v5/AutoTagging. ASP.NET Core's
// case-insensitive routing serves /api/v5/autotagging + /api/v5/autoTagging
// equivalently — the FE Redux thunks at autoTaggings.js use camelCase
// /autoTagging which keeps working without an explicit [Route(...)] override.
//
// Validator rules (Sonarr-canonical, preserved verbatim):
//   - Name not empty
//   - Name unique across rules (excluding self by Id)
//   - Tags not empty (per AT-02 spec — every rule must apply at least one tag)
//   - Specifications not empty (each rule must carry >= 1 Condition)
//   - Specifications.Name not whitespace (UI-friendly error surface for blank
//     condition labels)
//
// Service-side event publish: AutoTaggingService.Insert / Update / Delete each
// fire AutoTagsUpdatedEvent via IEventAggregator (24-02 restore, verified by
// AutoTaggingServiceFixture). The controller MUST NOT swallow these — every
// CRUD endpoint delegates straight to _autoTaggingService. AutoTaggingControllerFixture
// asserts the controller -> service flow does not short-circuit via dedicated
// Insert_publishes_event / Update_publishes_event / Delete_publishes_event tests.
[V5ApiController]
public class AutoTaggingController : RestController<AutoTaggingResource>
{
    private readonly IAutoTaggingService _autoTaggingService;
    private readonly List<IAutoTaggingSpecification> _specifications;

    public AutoTaggingController(IAutoTaggingService autoTaggingService,
                                 IEnumerable<IAutoTaggingSpecification> specifications)
    {
        _autoTaggingService = autoTaggingService;
        _specifications = specifications.ToList();

        SharedValidator.RuleFor(c => c.Name).NotEmpty();
        SharedValidator.RuleFor(c => c.Name)
            .Must((v, c) => !_autoTaggingService.All().Any(f => f.Name == c && f.Id != v.Id))
            .WithMessage("Must be unique.");
        SharedValidator.RuleFor(c => c.Tags).NotEmpty();
        SharedValidator.RuleFor(c => c).Custom((autoTag, context) =>
        {
            if (!autoTag.Specifications.Any())
            {
                context.AddFailure("Must contain at least one Condition");
            }

            if (autoTag.Specifications.Any(s => s.Name.IsNullOrWhiteSpace()))
            {
                context.AddFailure("Condition name(s) cannot be empty or consist of only spaces");
            }
        });
    }

    protected override AutoTaggingResource GetResourceById(int id)
    {
        return _autoTaggingService.GetById(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<AutoTaggingResource>> GetAll()
    {
        return TypedResults.Ok(_autoTaggingService.All().ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<AutoTaggingResource>, NotFound> Create([FromBody] AutoTaggingResource autoTagResource)
    {
        var model = autoTagResource.ToModel(_specifications);

        Validate(model);

        return TypedCreated(_autoTaggingService.Insert(model).Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<AutoTaggingResource>, NotFound> Update([FromBody] AutoTaggingResource resource)
    {
        var model = resource.ToModel(_specifications);

        Validate(model);

        _autoTaggingService.Update(model);

        return TypedAccepted(model.Id);
    }

    [RestDeleteById]
    public NoContent DeleteAutoTagging(int id)
    {
        _autoTaggingService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpGet("schema")]
    [Produces("application/json")]
    public Ok<List<AutoTaggingSpecificationResource>> GetTemplates()
    {
        return TypedResults.Ok(_specifications.OrderBy(x => x.Order).Select(x => x.ToResource()).ToList());
    }

    private void Validate(AutoTag definition)
    {
        foreach (var validationResult in definition.Specifications.Select(spec => spec.Validate()))
        {
            VerifyValidationResult(validationResult);
        }
    }

    private static void VerifyValidationResult(ValidationResult validationResult)
    {
        var result = new NzbDroneValidationResult(validationResult.Errors);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
