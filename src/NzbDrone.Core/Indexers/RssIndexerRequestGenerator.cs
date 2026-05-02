using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers
{
    public class RssIndexerRequestGenerator : IIndexerRequestGenerator
    {
        private readonly string _baseUrl;

        public RssIndexerRequestGenerator(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            pageableRequests.Add(new[] { new IndexerRequest(_baseUrl, HttpAccept.Rss) });

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria searchCriteria)
        {
            return new IndexerPageableRequestChain();
        }

        // Phase 3 D-01 — manga no-op overloads. TV indexers do not handle manga search;
        // ThingiProvider resolves indexer-by-protocol so these are never called via the
        // canonical pipeline. Returning an empty IndexerPageableRequestChain keeps the
        // contract satisfied (compile-error-driven fan-out documented in DIVERGENCE.md).
        public virtual IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria searchCriteria)
            => new IndexerPageableRequestChain();

        public virtual IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria searchCriteria)
            => new IndexerPageableRequestChain();
    }
}
