using System.Text.RegularExpressions;
using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Validation;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Tags;

// Phase 24 Plan 24-04 — IHandle<AutoTagsUpdatedEvent> RE-WIRED (AT-08). Sibling
// to the existing IHandle<TagsUpdatedEvent>. Phase 15 Plan 15-10 stripped this
// alongside the AutoTagging/ subtree delete; Phase 24 Plan 24-02 restored the
// subtree + Phase 24 Plan 24-03 shipped the 11-spec catalog + applier; this plan
// closes the loop by re-broadcasting SignalR Sync on every rule mutation so the
// FE /tag/detail query invalidates the moment AutoTagsUpdatedEvent fires.
[V5ApiController]
public class TagController : RestControllerWithSignalR<TagResource, Tag>,
                             IHandle<TagsUpdatedEvent>,
                             IHandle<AutoTagsUpdatedEvent>
{
    private readonly ITagService _tagService;
    private readonly TagInUseValidator _tagInUseValidator;

    public TagController(IBroadcastSignalRMessage signalRBroadcaster,
        ITagService tagService,
        TagInUseValidator tagInUseValidator)
        : base(signalRBroadcaster)
    {
        _tagService = tagService;
        _tagInUseValidator = tagInUseValidator;

        SharedValidator.RuleFor(c => c.Label).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Matches("^[a-z0-9-]+$", RegexOptions.IgnoreCase)
            .WithMessage("Allowed characters a-z, 0-9 and -");
    }

    protected override TagResource GetResourceById(int id)
    {
        return _tagService.GetTag(id).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<TagResource>> GetAll()
    {
        return TypedResults.Ok(_tagService.All().ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<TagResource>, NotFound> Create([FromBody] TagResource resource)
    {
        return TypedCreated(_tagService.Add(resource.ToModel()).Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<TagResource>, NotFound> Update([FromBody] TagResource resource)
    {
        _tagService.Update(resource.ToModel());
        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteTag(int id)
    {
        // Phase 22 D-02 — Mangarr divergence: validate DELETE against InUse via FluentValidation
        // (defense-in-depth on top of TagService.Delete()'s ModelConflictException throw).
        // RestController base does NOT run a DeleteValidator — OnActionExecuting only validates
        // on POST/PUT per RestController.cs:74-125; explicit pre-check is the only wiring.
        var tag = _tagService.GetTag(id);
        var validation = _tagInUseValidator.Validate(tag);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        _tagService.Delete(id);

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(TagsUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }

    // Phase 24 Plan 24-04 — AT-08 re-wire. Sibling to Handle(TagsUpdatedEvent).
    // AutoTaggingService.Insert / Update / Delete each publish this event;
    // re-broadcasting SignalR Sync invalidates the FE /tag/detail query so the
    // tag list reflects auto-tag changes without a manual refresh.
    [NonAction]
    public void Handle(AutoTagsUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }
}
