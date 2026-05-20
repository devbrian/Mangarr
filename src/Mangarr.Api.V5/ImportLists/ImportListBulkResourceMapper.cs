using Mangarr.Api.V5.Provider;
using NzbDrone.Core.ImportLists;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — Bulk-mapper sibling for ImportListBulkResource.
// Mirror of src/Mangarr.Api.V5/Indexers/IndexerBulkResource.cs (which carries its
// mapper inline). Each ?? short-circuit preserves the existing definition value
// when the payload omits the field — Sonarr-canonical "patch" semantics for the
// PUT /api/v5/importlist/bulk endpoint inherited from ProviderControllerBase.
public class ImportListBulkResourceMapper : ProviderBulkResourceMapper<ImportListBulkResource, ImportListDefinition>
{
    public override List<ImportListDefinition> UpdateModel(ImportListBulkResource resource, List<ImportListDefinition> existingDefinitions)
    {
        existingDefinitions.ForEach(existing =>
        {
            existing.EnableAutomaticAdd = resource.EnableAutomaticAdd ?? existing.EnableAutomaticAdd;
            existing.RootFolderPath = resource.RootFolderPath ?? existing.RootFolderPath;
            existing.TranslationProfileId = resource.TranslationProfileId ?? existing.TranslationProfileId;
            existing.CustomFormatProfileId = resource.CustomFormatProfileId ?? existing.CustomFormatProfileId;
        });

        return existingDefinitions;
    }
}
