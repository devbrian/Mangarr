using System.Collections.Generic;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// Composes API paths to comix.to's <c>/api/v1/...</c> JSON API.
    ///
    /// <para>
    /// Phase 17 Path A (per RESEARCH N-2 / Q-2): URL-with-token composition is gone.
    /// The runtime <see cref="IComixSigner"/> handles signing inside the warm Chromium
    /// page; <see cref="GetSearchRequests(MangaSearchCriteria)"/> populates the
    /// <see cref="ResolvedSignerPaths"/> collection with path-only strings, and
    /// <see cref="ComixIndexer"/> dispatches each path through
    /// <see cref="IComixSigner.ProxyFetchAsync"/> instead of going through the legacy
    /// <c>FetchReleases → IHttpClient</c> pipeline. The chain returned from
    /// <c>GetSearchRequests</c> is intentionally empty — its presence is preserved for
    /// the <see cref="IIndexerRequestGenerator"/> contract, but no IndexerRequests are
    /// emitted.
    /// </para>
    ///
    /// <para>
    /// Live shape pinned 2026-05-08 (comix-indexer-404 debug session): the API is
    /// <c>/api/v1/...</c> and the manga is keyed by an opaque <c>hid</c>
    /// (e.g. <c>"mr3m0"</c>) — NOT a slug. <see cref="ComixIndexer"/> resolves the
    /// hid via a search call before invoking <see cref="GetSearchRequests(MangaSearchCriteria)"/>;
    /// the resolved value is set on <see cref="ResolvedMangaHash"/> + <see cref="ResolvedMangaSlug"/>.
    /// </para>
    ///
    /// <para>
    /// The <see cref="GetRecentRequests"/> endpoint (latest-updates feed) does NOT
    /// require the signer — comix.to does not sign that endpoint per upstream
    /// keiyoushi behaviour. Wave 1 leaves it on the IndexerRequest path; the chain
    /// shape is preserved.
    /// </para>
    /// </summary>
    public class ComixRequestGenerator : IIndexerRequestGenerator
    {
        public ComixIndexerSettings Settings { get; set; }

        /// <summary>
        /// Phase 17 D-05 (per CONTEXT.md): process-singleton signer injected by
        /// <see cref="ComixIndexer"/> at <see cref="ComixIndexer.GetRequestGenerator"/>
        /// time. Replaces the static URL-with-token callsite that lived here pre-Phase-17
        /// (broken 2026-05-10 by upstream key rotation per
        /// .planning/debug/comix-invalid-token-403.md).
        /// </summary>
        public IComixSigner Signer { get; set; }

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

        /// <summary>
        /// Phase 17 (Plan 17-02 Task 2a per revision iteration 1, B-4): API paths the
        /// signer dispatcher (<see cref="ComixIndexer.Fetch(MangaSearchCriteria)"/> + the
        /// ChapterSearchCriteria overload) consumes. Populated by
        /// <see cref="GetSearchRequests(MangaSearchCriteria)"/> /
        /// <see cref="GetSearchRequests(ChapterSearchCriteria)"/>. Each entry is a
        /// path-only string suitable for <see cref="IComixSigner.ProxyFetchAsync"/>.
        /// </summary>
        internal IList<string> ResolvedSignerPaths { get; private set; } = new List<string>();

        public IndexerPageableRequestChain GetRecentRequests()
        {
            // Latest-updates feed: /api/v1/manga?order[chapter_updated_at]=desc&limit=50&page=1
            // Phase 17: this endpoint is NOT signed; stays on the legacy IndexerRequest path.
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
            // Phase 17 Path A: populate ResolvedSignerPaths with path-only strings; chain
            // returned empty (ComixIndexer.Fetch reads ResolvedSignerPaths and dispatches
            // through _signer.ProxyFetchAsync).
            ResolvedSignerPaths = new List<string>();

            if (string.IsNullOrWhiteSpace(ResolvedMangaHash))
            {
                return new IndexerPageableRequestChain();
            }

            var path = BuildChapterListPath(ResolvedMangaHash, ResolvedMangaSlug ?? ResolvedMangaHash);
            ResolvedSignerPaths.Add(path);

            return new IndexerPageableRequestChain();
        }

        public IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria sc)
        {
            // Same shape as MangaSearchCriteria — but appends &number=N when the criteria
            // carries a single chapter (server-side filter verified live 2026-05-08).
            ResolvedSignerPaths = new List<string>();

            if (string.IsNullOrWhiteSpace(ResolvedMangaHash))
            {
                return new IndexerPageableRequestChain();
            }

            decimal? chapterNumber = null;
            if (sc?.Chapters != null && sc.Chapters.Count == 1)
            {
                chapterNumber = sc.Chapters[0]?.ChapterNumber;
            }

            var path = BuildChapterListPath(
                ResolvedMangaHash,
                ResolvedMangaSlug ?? ResolvedMangaHash,
                chapterNumber: chapterNumber);
            ResolvedSignerPaths.Add(path);

            return new IndexerPageableRequestChain();
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape GetSearchRequests
        // overloads stripped per Plan 15-10 IndexerSearch/Definitions DELETE.

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Phase 17 D-08 (per CONTEXT.md): path-only — the signer's
        /// <see cref="IComixSigner.ProxyFetchAsync"/> prefixes <c>/api/v1</c> in-page
        /// and signs the path inside the Chromium context. Token query param is gone
        /// (Path A per RESEARCH N-2 / Q-2 — replaces the static URL-with-token callsite
        /// that lived here pre-Phase-17).
        ///
        /// <para>
        /// When <paramref name="chapterNumber"/> is set, an <c>&amp;number={value}</c>
        /// query parameter is appended; comix.to filters the response server-side to
        /// rows matching that chapter. Decimal chapter numbers (e.g., 12.5) round-trip
        /// via the <c>"0.###"</c> InvariantCulture format.
        /// </para>
        /// </summary>
        internal string BuildChapterListPath(string hash, string slug, int page = 1, decimal? chapterNumber = null)
        {
            // bracket query params must be URL-encoded so the request line stays valid:
            // order[number]=desc → order%5Bnumber%5D=desc. comix.to's parser still
            // un-encodes the brackets on the server.
            var path = $"/manga/{hash}/chapters"
                     + "?order%5Bnumber%5D=desc"
                     + "&limit=100"
                     + $"&page={page}"
                     + $"&mangaSlug={System.Net.WebUtility.UrlEncode(slug)}";

            if (chapterNumber.HasValue && chapterNumber.Value > 0m)
            {
                path += $"&number={System.Net.WebUtility.UrlEncode(chapterNumber.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))}";
            }

            return path;
        }

        /// <summary>
        /// Build the <c>/api/v1/manga?keyword=...</c> search URL used by
        /// <see cref="ComixIndexer"/> to resolve the manga title to its <c>hid</c>.
        /// This endpoint is unsigned (per upstream keiyoushi behaviour).
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
