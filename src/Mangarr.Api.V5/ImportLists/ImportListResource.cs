using Mangarr.Api.V5.Provider;
using NzbDrone.Core.ImportLists;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — V5 Resource DTO for ImportList providers.
// Modeled after src/Mangarr.Api.V5/Indexers/IndexerResource.cs (D-14 in-repo template
// analog). Field set drawn from src/NzbDrone.Core/ImportLists/ImportListDefinition.cs
// (Plan 26-04). Sonarr-shape divergences locked by Phase 26 CONTEXT.md:
//   * QualityProfileId → TranslationProfileId + CustomFormatProfileId (Phase 5 D-04)
//   * SeasonFolder / SeriesType — NOT carried over (TV-only; Migration 001 stripped)
//   * SearchForMissingEpisodes → SearchForMissingChapters (manga peer naming)
//
// T-26-05-02 mitigation: substrate POCO surfaces NO AccessToken/RefreshToken fields.
// Phase 27 provider Settings POCOs apply [FieldDefinition(Privacy = Password, Hidden)]
// annotations at the per-Settings field level via SchemaBuilder reflection.
public class ImportListResource : ProviderResource<ImportListResource>
{
    public bool EnableAutomaticAdd { get; set; }
    public bool SearchForMissingChapters { get; set; }
    public MonitorTypes ShouldMonitor { get; set; }
    public NewItemMonitorTypes MonitorNewItems { get; set; }
    public string? RootFolderPath { get; set; }
    public int TranslationProfileId { get; set; }
    public int CustomFormatProfileId { get; set; }
    public ImportListType ListType { get; set; }
    public TimeSpan MinRefreshInterval { get; set; }
}
