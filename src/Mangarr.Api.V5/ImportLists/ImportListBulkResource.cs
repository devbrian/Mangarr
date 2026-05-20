using Mangarr.Api.V5.Provider;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — Bulk-update DTO for the V5 ImportList controller's
// inherited PUT /api/v5/importlist/bulk endpoint. Mirror of
// src/Mangarr.Api.V5/Indexers/IndexerBulkResource.cs. Nullable fields support partial
// updates per ProviderControllerBase's bulk-update flow (only non-null fields
// overwrite existing definition state).
public class ImportListBulkResource : ProviderBulkResource<ImportListBulkResource>
{
    public bool? EnableAutomaticAdd { get; set; }
    public string? RootFolderPath { get; set; }
    public int? TranslationProfileId { get; set; }
    public int? CustomFormatProfileId { get; set; }
}
