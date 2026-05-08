using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape GetSearchRequests
    // overloads stripped per Plan 15-10 IndexerSearch/Definitions DELETE. Manga overloads canonical.
    public interface IIndexerRequestGenerator
    {
        IndexerPageableRequestChain GetRecentRequests();

        IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria searchCriteria);
    }
}
