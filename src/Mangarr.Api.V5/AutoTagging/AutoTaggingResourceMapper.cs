using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;

namespace Mangarr.Api.V5.AutoTagging;

// Phase 24 Plan 24-04 — V5 AutoTaggingResourceMapper port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160). Static-extension shape
// mirrors Phase 23's DelayProfileResourceMapper precedent (separate file from the
// Resource definition).
//
// ToResource: round-trips each AutoTag.Specifications entry through
// AutoTaggingSpecificationSchemaMapper.ToSchema (uses SchemaBuilder reflection on
// [FieldDefinition] attributes — Sonarr-canonical algorithm preserved verbatim).
// ToModel: looks up the concrete IAutoTaggingSpecification impl by class name from
// the injected catalog (DryIoc-resolved 11-spec list from 24-03), instantiates a
// fresh instance via SchemaBuilder.ReadFromSchema, copies Name/Negate/Required.
//
// The MapSpecification helper throws ArgumentException on unknown Implementation
// strings — surface a 400 via FluentValidation handling in the controller (Sonarr
// V5 source raises the same exception class for the same surface).
public static class AutoTaggingResourceMapper
{
    public static AutoTaggingResource ToResource(this AutoTag model)
    {
        return new AutoTaggingResource
        {
            Id = model.Id,
            Name = model.Name,
            RemoveTagsAutomatically = model.RemoveTagsAutomatically,
            Tags = model.Tags ?? new HashSet<int>(),
            Specifications = model.Specifications?.Select(x => x.ToResource()).ToList() ?? new List<AutoTaggingSpecificationResource>()
        };
    }

    public static List<AutoTaggingResource> ToResource(this IEnumerable<AutoTag> models)
    {
        return models?.Select(m => m.ToResource()).ToList() ?? new List<AutoTaggingResource>();
    }

    public static AutoTag ToModel(this AutoTaggingResource resource, List<IAutoTaggingSpecification> specifications)
    {
        return new AutoTag
        {
            Id = resource.Id,
            Name = resource.Name,
            RemoveTagsAutomatically = resource.RemoveTagsAutomatically,
            Tags = resource.Tags ?? new HashSet<int>(),
            Specifications = resource.Specifications?.Select(x => x.ToModel(specifications)).ToList() ?? new List<IAutoTaggingSpecification>()
        };
    }
}
