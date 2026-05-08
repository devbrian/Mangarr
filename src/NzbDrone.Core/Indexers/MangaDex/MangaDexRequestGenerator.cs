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
            // MangaDex's GET /chapter endpoint supports simultaneous filtering by
            // manga + chapter[] + translatedLanguage[] (verified 2026-05-08 against
            // https://api.mangadex.org/docs/static/api.yaml). Hitting the point query
            // returns only releases for the targeted chapter — no whole-feed fan-out,
            // no client-side filter required. The previous "MangaDex does not expose
            // a single-chapter point query" assumption (Phase 3 RESEARCH §612) was
            // incorrect; the response is bounded to the requested chapter and the
            // SourceKey rate budget is preserved (one request per chapter search).
            if (sc?.Manga?.MangaDexId == null)
            {
                return new IndexerPageableRequestChain();
            }

            var url = Api?.BuildChapterPointQueryUrl(sc.Manga.MangaDexId.Value, sc.ChapterNumber, sc.PreferredLanguages)
                ?? $"{Settings.BaseUrl.TrimEnd('/')}/chapter?manga={sc.Manga.MangaDexId.Value:D}&chapter[]={System.Net.WebUtility.UrlEncode(sc.ChapterNumber.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))}&order[publishAt]=desc&limit=100&includes[]=scanlation_group&includes[]=manga&contentRating[]=safe&contentRating[]=suggestive&translatedLanguage[]=en";
            var chain = new IndexerPageableRequestChain();
            chain.Add(new[] { new IndexerRequest(url, HttpAccept.Json) });
            return chain;
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape GetSearchRequests
        // overloads stripped per Plan 15-10 IndexerSearch/Definitions DELETE.
    }
}
