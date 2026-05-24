using Mangarr.Api.V5.Provider;
using NzbDrone.Core.Metadata;

namespace Mangarr.Api.V5.Metadata;

// Phase 30 Plan 30-04 D-02 — Bulk-update DTO + Mapper for the V5 Metadata
// controller. Empty placeholders per RESEARCH §1 + ConnectionController
// precedent: bulk endpoints are disabled via [NonAction] overrides on
// MetadataController so this resource never deserializes a real client payload.
// The placeholders exist solely to satisfy the ProviderControllerBase generic
// type constraints.
//
// If v1.3+ exposes per-MetadataDefinition advanced settings (tag filter,
// per-format options) and bulk editing materializes as a UX requirement, this
// resource can grow nullable fields mirroring ImportListBulkResource's
// EnableAutomaticAdd / RootFolderPath / TranslationProfileId / etc. pattern.
public class MetadataBulkResource : ProviderBulkResource<MetadataBulkResource>
{
}

public class MetadataBulkResourceMapper : ProviderBulkResourceMapper<MetadataBulkResource, MetadataDefinition>
{
}
