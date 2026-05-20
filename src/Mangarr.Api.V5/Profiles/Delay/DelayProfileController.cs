using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Mangarr.Http.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.Delay;

namespace Mangarr.Api.V5.Profiles.Delay;

// Phase 23 Plan 23-02 — V5 DelayProfileController port from Sonarr V3 source pinned
// at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 (see 23-02-PORT-SOURCE.md).
//
// Base class decision (A2/D-10): RestController<DelayProfileResource> (plain — NOT
// RestControllerWithSignalR<,>) per the pinned Sonarr V3 source. NO SignalR
// substrate, NO DelayProfilesUpdatedEvent, NO SignalRListener.tsx branch edit.
//
// Validator decisions (Q5 + Sonarr-canonical preservation):
// - RuleFor(d => d.Tags).NotEmpty().When(d => d.Id != 1) — Sonarr-canonical: non-default
//   profiles must carry ≥1 tag (the tag is the cross-cutting selector for which profile
//   applies to which manga).
// - RuleFor(d => d.Tags).EmptyCollection<DelayProfileResource, int>().When(d => d.Id == 1)
//   — Sonarr-canonical: the global default (Id=1, Order=int.MaxValue per the seeder at
//   DelayProfileService.Handle:198-216) is tag-less by definition.
// - RuleFor(d => d.Tags).SetValidator(tagInUseValidator) — cross-row uniqueness
//   (Phase 22 D-03 live consumer #2 of the Tag substrate; validator already shipped).
// - NOT ported: RuleFor(d => d.UsenetDelay).GreaterThanOrEqualTo(0) — Resource OMITS
//   per Phase 23 D-03; Phase 26 Plan 26-03 (DP-02) dropped the entity prop + Migration 003
//   dropped the DB column. Field no longer exists.
// - NOT ported: RuleFor(d => d.TorrentDelay).GreaterThanOrEqualTo(0) — same lifecycle.
// - NOT ported: custom (EnableUsenet || EnableTorrent) rule — entity props dropped by
//   Phase 26 Plan 26-03 (DP-02); rule no longer has fields to evaluate.
//
// DeleteProfile preserves Sonarr's "Id=1 is undeletable" guard via MethodNotAllowedException
// (Mangarr.Http.REST). DelayProfileService.Delete does NOT carry this guard built-in;
// deleting Id=1 would re-introduce the .First() crash class that issue #62 + the
// Pattern S4 seeder fix.
[V5ApiController]
public class DelayProfileController : RestController<DelayProfileResource>
{
    private readonly IDelayProfileService _delayProfileService;

    public DelayProfileController(IDelayProfileService delayProfileService,
                                  DelayProfileTagInUseValidator tagInUseValidator)
    {
        _delayProfileService = delayProfileService;

        SharedValidator.RuleFor(d => d.Tags).NotEmpty().When(d => d.Id != 1);
        SharedValidator.RuleFor(d => d.Tags).EmptyCollection<DelayProfileResource, int>().When(d => d.Id == 1);
        SharedValidator.RuleFor(d => d.Tags).SetValidator(tagInUseValidator);
    }

    [RestPostById]
    [Consumes("application/json")]
    public Results<Created<DelayProfileResource>, NotFound> Create([FromBody] DelayProfileResource resource)
    {
        var model = resource.ToModel();
        model = _delayProfileService.Add(model!);

        return TypedCreated(model.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public Results<Accepted<DelayProfileResource>, NotFound> Update([FromBody] DelayProfileResource resource)
    {
        var model = resource.ToModel();
        _delayProfileService.Update(model!);

        return TypedAccepted(model!.Id);
    }

    [RestDeleteById]
    public NoContent DeleteProfile(int id)
    {
        if (id == 1)
        {
            throw new MethodNotAllowedException("Cannot delete global delay profile");
        }

        _delayProfileService.Delete(id);

        return TypedResults.NoContent();
    }

    protected override DelayProfileResource GetResourceById(int id)
    {
        return _delayProfileService.Get(id).ToResource()!;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<DelayProfileResource>> GetAll()
    {
        return TypedResults.Ok(_delayProfileService.All().ToResource());
    }

    [HttpPut("reorder/{id:int}")]
    public Ok<List<DelayProfileResource>> Reorder([FromRoute] int id, [FromQuery] int? after)
    {
        var profiles = _delayProfileService.Reorder(id, after);
        return TypedResults.Ok(profiles.ToResource());
    }
}
