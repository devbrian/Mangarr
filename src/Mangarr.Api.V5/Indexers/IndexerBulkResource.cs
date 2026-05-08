using Mangarr.Api.V5.Provider;
using NzbDrone.Core.Indexers;

namespace Mangarr.Api.V5.Indexers;

public class IndexerBulkResource : ProviderBulkResource<IndexerBulkResource>
{
    public bool? EnableRss { get; set; }
    public bool? EnableAutomaticSearch { get; set; }
    public bool? EnableInteractiveSearch { get; set; }
    public int? Priority { get; set; }

    // Sonarr divergence: Phase 15 D-13 + D-22 — SeasonSearchMaximumSingleEpisodeAge property
    // removed from bulk resource (TV-only; column dropped). Debug session: mangadex-save-fails.
}

public class IndexerBulkResourceMapper : ProviderBulkResourceMapper<IndexerBulkResource, IndexerDefinition>
{
    public override List<IndexerDefinition> UpdateModel(IndexerBulkResource resource, List<IndexerDefinition> existingDefinitions)
    {
        existingDefinitions.ForEach(existing =>
        {
            existing.EnableRss = resource.EnableRss ?? existing.EnableRss;
            existing.EnableAutomaticSearch = resource.EnableAutomaticSearch ?? existing.EnableAutomaticSearch;
            existing.EnableInteractiveSearch = resource.EnableInteractiveSearch ?? existing.EnableInteractiveSearch;
            existing.Priority = resource.Priority ?? existing.Priority;
        });

        return existingDefinitions;
    }
}
