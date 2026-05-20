using Mangarr.Api.V5.Provider;
using NzbDrone.Core.ImportLists;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — Mapper sibling for ImportListResource. Round-trips
// the substrate-only field set between V5 Resource (FE-facing DTO) and the
// ImportListDefinition (DB-backed model from Plan 26-04). Modeled after
// src/Mangarr.Api.V5/Indexers/IndexerResource.cs (which keeps the Mapper inline
// with the Resource); split into its own file here per Plan 26-05 files_modified
// for parity with the Sonarr v5-develop layout and to keep Resource POCOs free
// of mapping logic when the substrate grows.
public class ImportListResourceMapper : ProviderResourceMapper<ImportListResource, ImportListDefinition>
{
    public override ImportListResource ToResource(ImportListDefinition definition)
    {
        var resource = base.ToResource(definition);

        resource.EnableAutomaticAdd = definition.EnableAutomaticAdd;
        resource.SearchForMissingChapters = definition.SearchForMissingChapters;
        resource.ShouldMonitor = definition.ShouldMonitor;
        resource.MonitorNewItems = definition.MonitorNewItems;
        resource.RootFolderPath = definition.RootFolderPath;
        resource.TranslationProfileId = definition.TranslationProfileId;
        resource.CustomFormatProfileId = definition.CustomFormatProfileId;
        resource.ListType = definition.ListType;
        resource.MinRefreshInterval = definition.MinRefreshInterval;

        return resource;
    }

    public override ImportListDefinition ToModel(ImportListResource resource, ImportListDefinition? existingDefinition)
    {
        var definition = base.ToModel(resource, existingDefinition);

        definition.EnableAutomaticAdd = resource.EnableAutomaticAdd;
        definition.SearchForMissingChapters = resource.SearchForMissingChapters;
        definition.ShouldMonitor = resource.ShouldMonitor;
        definition.MonitorNewItems = resource.MonitorNewItems;
        definition.RootFolderPath = resource.RootFolderPath;
        definition.TranslationProfileId = resource.TranslationProfileId;
        definition.CustomFormatProfileId = resource.CustomFormatProfileId;

        // ListType + MinRefreshInterval are runtime-only ([MemberwiseEqualityIgnore]
        // on the definition); SetProviderCharacteristics in ImportListFactory writes
        // them from the live provider — do NOT copy them back to the model on save.

        return definition;
    }
}
