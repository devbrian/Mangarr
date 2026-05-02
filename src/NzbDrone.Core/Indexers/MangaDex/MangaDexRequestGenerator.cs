using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// Composes HTTP requests to MangaDex's published JSON API. Manga-shaped overloads
    /// (<see cref="GetSearchRequests(MangaSearchCriteria)"/> +
    /// <see cref="GetSearchRequests(ChapterSearchCriteria)"/>) emit URLs against
    /// <c>/manga/{id}/feed</c>; the global recent-updates feed targets <c>/chapter</c>.
    /// All 7 inherited TV overloads return an empty
    /// <see cref="IndexerPageableRequestChain"/> per Plan 03-02 Pattern 3 fan-out (D-03).
    ///
    /// <para>
    /// Context: when <c>MangaSearchCriteria.Manga.MangaDexId</c> is null (cross-resolve
    /// gap from Phase 2 D-19..D-22 fallback), no MangaDex request is possible — the chain
    /// returns empty and the upstream <c>FetchReleases</c> short-circuits.
    /// </para>
    /// </summary>
    public class MangaDexRequestGenerator : IIndexerRequestGenerator
    {
        public MangaDexIndexerSettings Settings { get; set; }
        public MangaDexIndexerApi Api { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var url = Api?.BuildRecentUrl()
                ?? $"{Settings.BaseUrl.TrimEnd('/')}/chapter?translatedLanguage[]=en&order[publishAt]=desc&limit=100&includes[]=scanlation_group&includes[]=manga&contentRating[]=safe&contentRating[]=suggestive";
            var chain = new IndexerPageableRequestChain();
            chain.Add(new[] { new IndexerRequest(url, HttpAccept.Json) });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria sc)
        {
            // MangaDex needs the manga UUID; if not cross-resolved (Phase 2 D-19..D-22 fallback),
            // we cannot fetch from MangaDex — return empty chain so FetchReleases yields zero
            // releases (NOT an exception — manga that lacks a MangaDexId is a normal state for
            // sources discovered via AniList / MAL).
            if (sc?.Manga?.MangaDexId == null)
            {
                return new IndexerPageableRequestChain();
            }

            var url = Api?.BuildFeedUrl(sc.Manga.MangaDexId.Value, sc.PreferredLanguages)
                ?? $"{Settings.BaseUrl.TrimEnd('/')}/manga/{sc.Manga.MangaDexId.Value:D}/feed?limit=500&order[chapter]=asc&includes[]=scanlation_group&includes[]=manga&contentRating[]=safe&contentRating[]=suggestive&translatedLanguage[]=en";
            var chain = new IndexerPageableRequestChain();
            chain.Add(new[] { new IndexerRequest(url, HttpAccept.Json) });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria sc)
        {
            // Reuse the whole-manga feed; the parser filters by ChapterNumber + TranslatedLanguage
            // client-side. MangaDex does not expose a single-chapter point query, and reusing the
            // feed saves the SourceKey rate budget vs N point-queries — Phase 4 image-fetch is
            // already on the same bucket and cannot afford the burst.
            if (sc?.Manga?.MangaDexId == null)
            {
                return new IndexerPageableRequestChain();
            }

            return GetSearchRequests(new MangaSearchCriteria
            {
                Manga = sc.Manga,
                Chapters = sc.Chapters,
                PreferredLanguages = sc.PreferredLanguages
            });
        }

        // ── 7 TV overloads — return empty chain (D-03 fan-out per Plan 03-02 Pattern 3) ────
        public IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria sc) => new IndexerPageableRequestChain();
        public IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria sc) => new IndexerPageableRequestChain();
    }
}
