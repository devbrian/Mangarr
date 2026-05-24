using Mangarr.Api.V5.Provider;

namespace Mangarr.Api.V5.Metadata;

// Phase 30 Plan 30-04 D-02 — V5 Resource DTO for IMetadata providers. Modeled
// after src/Mangarr.Api.V5/ImportLists/ImportListResource.cs (PATTERNS.md
// §Plan 30-04) with a single Enable bool field per D-04 single-toggle UX.
//
// Provider base fields (Name, Implementation, ConfigContract, Fields, Tags,
// Message, InfoLink, Presets) inherited from ProviderResource<T>. Settings POCO
// fields are surfaced via SchemaBuilder reflection into the Fields[] collection
// — for ComicInfoMetadataSettings that array is empty in v1.2 (D-04).
//
// **LOCKED file layout:** this file contains the POCO only. The mapper class
// lives in a sibling MetadataResourceMapper.cs file (matches the in-repo
// ImportListResourceMapper.cs convention per PATTERNS.md).
public class MetadataResource : ProviderResource<MetadataResource>
{
    public bool Enable { get; set; }
}
