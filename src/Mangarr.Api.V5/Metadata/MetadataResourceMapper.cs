using Mangarr.Api.V5.Provider;
using NzbDrone.Core.Metadata;

namespace Mangarr.Api.V5.Metadata;

// Phase 30 Plan 30-04 D-02 — Mapper sibling for MetadataResource. Round-trips
// the single Enable bool between V5 Resource (FE-facing DTO) and the
// MetadataDefinition (DB-backed model). Modeled after
// src/Mangarr.Api.V5/ImportLists/ImportListResourceMapper.cs per PATTERNS.md
// §Plan 30-04.
//
// Sonarr-canonical Enable round-trip — no FK rebind / no runtime-only fields
// to suppress (MetadataDefinition has no [MemberwiseEqualityIgnore] runtime
// fields like ImportListDefinition's ListType / MinRefreshInterval).
public class MetadataResourceMapper : ProviderResourceMapper<MetadataResource, MetadataDefinition>
{
    public override MetadataResource ToResource(MetadataDefinition definition)
    {
        var resource = base.ToResource(definition);

        resource.Enable = definition.Enable;

        return resource;
    }

    public override MetadataDefinition ToModel(MetadataResource resource, MetadataDefinition? existingDefinition)
    {
        var definition = base.ToModel(resource, existingDefinition);

        definition.Enable = resource.Enable;

        return definition;
    }
}
