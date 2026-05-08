using NzbDrone.Core.MetadataSource;
using Mangarr.Api.V5.Provider;

namespace Mangarr.Api.V5.MetadataSource;

// Phase 2 developer-surface DTO per Plan 02-10. Mirrors IndexerResource shape verbatim
// (Mangarr.Api.V5/Indexers/IndexerResource.cs) with the single divergence: an
// `IsPrimary` bool that surfaces D-15's at-most-one invariant flag. The invariant
// itself is enforced inside MetadataSourceFactory.SetPrimary, NOT here.
public class MetadataSourceResource : ProviderResource<MetadataSourceResource>
{
    public bool IsPrimary { get; set; }
}

public class MetadataSourceResourceMapper : ProviderResourceMapper<MetadataSourceResource, MetadataSourceDefinition>
{
    public override MetadataSourceResource ToResource(MetadataSourceDefinition definition)
    {
        if (definition == null)
        {
            return null!;
        }

        var resource = base.ToResource(definition);
        resource.IsPrimary = definition.IsPrimary;
        return resource;
    }

    public override MetadataSourceDefinition ToModel(MetadataSourceResource resource, MetadataSourceDefinition? existingDefinition)
    {
        if (resource == null)
        {
            return null!;
        }

        var definition = base.ToModel(resource, existingDefinition);
        definition.IsPrimary = resource.IsPrimary;
        return definition;
    }
}

// BulkResource — minimal (only the single bool toggle); mirrors IndexerBulkResource shape.
public class MetadataSourceBulkResource : ProviderBulkResource<MetadataSourceBulkResource>
{
    public bool? IsPrimary { get; set; }
}

public class MetadataSourceBulkResourceMapper : ProviderBulkResourceMapper<MetadataSourceBulkResource, MetadataSourceDefinition>
{
    public override List<MetadataSourceDefinition> UpdateModel(MetadataSourceBulkResource resource, List<MetadataSourceDefinition> existingDefinitions)
    {
        if (resource == null)
        {
            return new List<MetadataSourceDefinition>();
        }

        if (resource.IsPrimary.HasValue)
        {
            // Bulk IsPrimary is intentionally NOT applied here — the D-15 invariant requires
            // a dedicated SetPrimary(int id) flow that demotes all others atomically. Bulk
            // promotion would violate the invariant. Surface only for symmetry with the
            // ProviderBulkResource shape.
        }

        return existingDefinitions;
    }
}
