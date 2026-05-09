using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// Composes HTTP requests to comix.to's <c>/api/v1/...</c> JSON API.
    ///
    /// <para>
    /// The Phase 3 plan literal targeted <c>/api/v2/...</c> against a slugified title key
    /// (e.g. <c>/api/v2/manga/the-forgotten-field/chapters</c>). Live-API verification on
    /// 2026-05-08 (during the comix-indexer-404 debug session) showed the actual API is
    /// <c>/api/v1/...</c> and the manga is keyed by an opaque <c>hid</c>
    /// (e.g. <c>"mr3m0"</c>) — NOT a slug. The <see cref="ComixIndexer"/> resolves the
    /// hid via a search call before invoking <see cref="GetSearchRequests(MangaSearchCriteria)"/>;
    /// the resolved value is set on <see cref="ResolvedMangaHash"/> + <see cref="ResolvedMangaSlug"/>.
    /// </para>
    ///
    /// <para>
    /// Chapter-list URLs additionally require a <c>_=&lt;token&gt;</c> query parameter where
    /// the token is <see cref="ComixHash.GenerateHash"/> applied to the URL path (without
    /// the <c>/api/v1</c> prefix). Without this token the endpoint returns 403
    /// <c>"Missing token."</c>. The token derivation is portrered verbatim from keiyoushi's
    /// <c>Hash.kt</c>.
    /// </para>
    ///
    /// <para>
    /// Referer is applied per-request (D-14 / keiyoushi <c>headersBuilder()</c>);
    /// HttpAggregatorBase additionally sets <c>RateLimitKey=SourceKey</c> + honest UA on
    /// dispatch — double-applying Referer is safe.
    /// </para>
    /// </summary>
    public class ComixRequestGenerator : IIndexerRequestGenerator
    {
        public ComixIndexerSettings Settings { get; set; }

        /// <summary>
        /// Opaque comix.to hid (e.g. "mr3m0") for the manga whose chapter list we're fetching.
        /// Set by <see cref="ComixIndexer.Fetch(MangaSearchCriteria)"/> after resolving via
        /// the <c>/api/v1/manga?keyword=&lt;title&gt;</c> search endpoint. Null when no
        /// matching manga was found — <see cref="GetSearchRequests(MangaSearchCriteria)"/>
        /// returns an empty chain in that case.
        /// </summary>
        public string ResolvedMangaHash { get; set; }

        /// <summary>
        /// Optional <c>{hid}-{slug}</c> form (e.g. "mr3m0-the-forgotten-field") that comix.to's
        /// chapter-list endpoint accepts via the <c>mangaSlug=</c> query parameter. keiyoushi
        /// passes this for forward-compat with their reader-page deep-links; comix.to ignores
        /// it when absent. Defaults to the bare hid if no resolved slug is set.
        /// </summary>
        public string ResolvedMangaSlug { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            // Latest-updates feed: /api/v1/manga?order[chapter_updated_at]=desc&limit=50&page=1
            // RESEARCH.md Q-1 recommends order[chapter_updated_at]=desc over views_30d — more
            // relevant to Wanted polling. NO token required for this endpoint.
            var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/v1/manga"
                    + "?order%5Bchapter_updated_at%5D=desc"
                    + "&limit=50"
                    + "&page=1";

            var chain = new IndexerPageableRequestChain();
            var req = new IndexerRequest(url, HttpAccept.Json);
            req.HttpRequest.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";
            chain.Add(new[] { req });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(MangaSearchCriteria sc)
        {
            // The hid lookup is performed by ComixIndexer.Fetch() before dispatching — by the
            // time we land here, ResolvedMangaHash is either populated or null. If null, the
            // manga has no comix.to mapping (or the search returned no hits) — return an
            // empty chain so FetchReleases short-circuits to an empty release list.
            if (string.IsNullOrWhiteSpace(ResolvedMangaHash))
            {
                return new IndexerPageableRequestChain();
            }

            var url = BuildChapterListUrl(ResolvedMangaHash, ResolvedMangaSlug ?? ResolvedMangaHash);

            var chain = new IndexerPageableRequestChain();
            var req = new IndexerRequest(url, HttpAccept.Json);
            req.HttpRequest.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";
            chain.Add(new[] { req });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria sc)
        {
            // comix.to's /api/v1/manga/{hid}/chapters endpoint accepts an undocumented
            // &number={chapterNumber} query parameter that filters the response server-side
            // to rows matching that chapter. Verified live 2026-05-08 against
            //   https://comix.to/api/v1/manga/gmyj7/chapters?...&number=1
            // (6 items returned, all chapter 1) vs the same call without &number= (97 items).
            // The token derivation pins to the path only ("/manga/{hash}/chapters"), so
            // adding &number= does NOT invalidate the anti-bot signature. A previous
            // assumption (Phase 3 RESEARCH) that comix.to lacked a per-chapter filter
            // turned out to be incorrect.
            if (string.IsNullOrWhiteSpace(ResolvedMangaHash))
            {
                return new IndexerPageableRequestChain();
            }

            var url = BuildChapterListUrl(ResolvedMangaHash, ResolvedMangaSlug ?? ResolvedMangaHash, chapterNumber: sc?.ChapterNumber);

            var chain = new IndexerPageableRequestChain();
            var req = new IndexerRequest(url, HttpAccept.Json);
            req.HttpRequest.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";
            chain.Add(new[] { req });
            return chain;
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape GetSearchRequests
        // overloads stripped per Plan 15-10 IndexerSearch/Definitions DELETE.

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build a fully-qualified chapter-list URL with the comix.to anti-bot token.
        /// Mirrors keiyoushi's <c>chapterListRequest</c>.
        ///
        /// <para>
        /// When <paramref name="chapterNumber"/> is set, an <c>&amp;number={value}</c> query
        /// parameter is appended; comix.to filters the response server-side to rows matching
        /// that chapter. The token derivation pins to the path only, so the filter does not
        /// invalidate the anti-bot signature. Decimal chapter numbers (e.g., 12.5) round-trip
        /// via the <c>"0.###"</c> InvariantCulture format.
        /// </para>
        /// </summary>
        internal string BuildChapterListUrl(string hash, string slug, int page = 1, decimal? chapterNumber = null)
        {
            // The token signs the path BEFORE the /api/v1 prefix is appended (i.e. comix.to
            // computes the token from "/manga/{hid}/chapters" only — verified against the
            // live JS bundle's request signing helper).
            var pathForToken = $"/manga/{hash}/chapters";
            var token = ComixHash.GenerateHash(pathForToken);

            // bracket query params must be URL-encoded so the request line stays valid:
            // order[number]=desc → order%5Bnumber%5D=desc. comix.to's parser still
            // un-encodes the brackets on the server.
            var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/v1/manga/{hash}/chapters"
                    + "?order%5Bnumber%5D=desc"
                    + "&limit=100"
                    + $"&page={page}"
                    + $"&_={System.Net.WebUtility.UrlEncode(token)}"
                    + $"&mangaSlug={System.Net.WebUtility.UrlEncode(slug)}";

            if (chapterNumber.HasValue && chapterNumber.Value > 0m)
            {
                url += $"&number={System.Net.WebUtility.UrlEncode(chapterNumber.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))}";
            }

            return url;
        }

        /// <summary>
        /// Build the <c>/api/v1/manga?keyword=...</c> search URL used by
        /// <see cref="ComixIndexer"/> to resolve the manga title to its <c>hid</c>.
        /// </summary>
        internal string BuildSearchUrl(string keyword)
        {
            return $"{Settings.BaseUrl.TrimEnd('/')}/api/v1/manga"
                 + $"?keyword={System.Net.WebUtility.UrlEncode(keyword ?? string.Empty)}"
                 + "&limit=10"
                 + "&page=1";
        }
    }
}
