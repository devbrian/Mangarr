// Sonarr divergence: Phase 15 Plan 15-10 — AutoTagging/ DELETED.
//   using NzbDrone.Core.AutoTagging; ← deleted
using System.Text.RegularExpressions;
using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Tags;

// Sonarr divergence: Phase 15 Plan 15-10 — IHandle<AutoTagsUpdatedEvent> stripped (event DELETED).
[V5ApiController]
public class TagController : RestControllerWithSignalR<TagResource, Tag>,
                             IHandle<TagsUpdatedEvent>
{
    private readonly ITagService _tagService;

    public TagController(IBroadcastSignalRMessage signalRBroadcaster,
        ITagService tagService)
        : base(signalRBroadcaster)
    {
        _tagService = tagService;

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
        _tagService.Delete(id);

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(TagsUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }

    // Sonarr divergence: Phase 15 Plan 15-10 — Handle(AutoTagsUpdatedEvent) stripped (event DELETED).
}
