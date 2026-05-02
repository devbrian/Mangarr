using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerRequestGenerator
    {
        IndexerPageableRequestChain GetRecentRequests();

        // ── Existing TV overloads (UNCHANGED) ─────────────────────────────
        IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria searchCriteria);

        // ── NEW manga overloads (Phase 3; mirrors IIndexer.Fetch shape) ───
        // Existing TV *RequestGenerator implementations gain trivial no-op overloads in Task 3
        // (compile-error-driven fan-out — documented in DIVERGENCE.md by Plan 03-06).
        IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria searchCriteria);
        IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria searchCriteria);
    }
}
