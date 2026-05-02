using System.Net;
using System.Text;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// Composes HTTP requests to comix.to's <c>/api/v2/...</c> JSON API. Manga-shaped
    /// overloads (<see cref="GetSearchRequests(MangaSearchCriteria)"/> +
    /// <see cref="GetSearchRequests(ChapterSearchCriteria)"/>) emit URLs against
    /// <c>/api/v2/manga/{hash}/chapters</c>; the latest-updates feed targets
    /// <c>/api/v2/manga?order[chapter_updated_at]=desc</c>. All 7 inherited TV overloads
    /// return an empty <see cref="IndexerPageableRequestChain"/> per Plan 03-02 Pattern 3
    /// fan-out (D-03).
    ///
    /// <para>
    /// Hash resolution: comix.to keys per-manga chapter lists by an opaque
    /// <c>hash_id</c> (e.g. <c>"1mrl"</c>, <c>"0krjl"</c>) rather than the integer
    /// <c>manga_id</c>. The Phase 2 <c>Manga</c> model carries no <c>ComixHash</c> field;
    /// for the v1 minimum-viable port, we derive a slug from
    /// <see cref="Manga.Manga.CleanTitle"/> (or fallback <see cref="Manga.Manga.Title"/>)
    /// — comix.to historically accepts the title-slug as a chapter-list lookup key. v2
    /// may add a comix.to metadata source for cross-resolution to the canonical
    /// <c>hash_id</c> (Phase 2 deferred).
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

        public IndexerPageableRequestChain GetRecentRequests()
        {
            // Latest-updates feed: /api/v2/manga?order[chapter_updated_at]=desc&limit=50&page=1
            // RESEARCH.md Q-1 recommends order[chapter_updated_at]=desc over views_30d — more
            // relevant to Wanted polling.
            var url = $"{Settings.BaseUrl.TrimEnd('/')}/api/v2/manga"
                    + "?order[chapter_updated_at]=desc"
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
            // comix.to expects an opaque hash_id. Manga model has no ComixHash field (v2 work);
            // use a title-slug fallback. If no usable title, return empty chain so FetchReleases
            // short-circuits to an empty release list (NOT an exception — manga lacking a comix
            // mapping is a normal state pre-cross-resolution).
            var hash = ResolveHashKey(sc?.Manga);
            if (hash == null)
            {
                return new IndexerPageableRequestChain();
            }

            var url = BuildChapterListUrl(hash);

            var chain = new IndexerPageableRequestChain();
            var req = new IndexerRequest(url, HttpAccept.Json);
            req.HttpRequest.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";
            chain.Add(new[] { req });
            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ChapterSearchCriteria sc)
        {
            // Reuse the whole-manga chapter-list; the parser filters by ChapterNumber +
            // TranslatedLanguage client-side. comix.to does not expose a single-chapter point
            // query, and reusing the list saves the SourceKey rate budget vs N point-queries —
            // Phase 4 image-fetch is already on the same bucket and cannot afford the burst.
            return GetSearchRequests(new MangaSearchCriteria
            {
                Manga = sc?.Manga,
                Chapters = sc?.Chapters,
                PreferredLanguages = sc?.PreferredLanguages
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

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        private string BuildChapterListUrl(string hash)
            => $"{Settings.BaseUrl.TrimEnd('/')}/api/v2/manga/{hash}/chapters"
             + "?order[number]=desc&limit=100&page=1";

        /// <summary>
        /// Resolve a comix.to lookup key from a <see cref="Manga.Manga"/>. Order:
        /// <list type="number">
        /// <item><c>CleanTitle</c> → slug</item>
        /// <item><c>Title</c> → slug (fallback)</item>
        /// </list>
        /// Returns <c>null</c> when no usable title is available — caller should emit an empty
        /// request chain in that case.
        /// </summary>
        internal static string ResolveHashKey(Manga.Manga manga)
        {
            if (manga == null)
            {
                return null;
            }

            var source = !string.IsNullOrWhiteSpace(manga.CleanTitle) ? manga.CleanTitle : manga.Title;
            return string.IsNullOrWhiteSpace(source) ? null : Slugify(source);
        }

        /// <summary>
        /// Naive title-to-slug: lowercase ASCII letters + digits separated by single dashes.
        /// Mirrors comix.to's slug convention (e.g. "One Piece" → "one-piece"). Non-ASCII is
        /// dropped silently (comix.to cannot resolve non-Latin titles via slug; the v2
        /// metadata-source linkage will replace this fallback with canonical hash_id lookup).
        /// </summary>
        internal static string Slugify(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var sb = new StringBuilder(input.Length);
            var lastWasDash = true; // suppress leading dashes
            foreach (var raw in input)
            {
                var c = char.ToLowerInvariant(raw);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                    lastWasDash = false;
                }
                else if (!lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            // Trim trailing dash
            while (sb.Length > 0 && sb[sb.Length - 1] == '-')
            {
                sb.Length--;
            }

            return sb.Length == 0 ? null : WebUtility.UrlEncode(sb.ToString());
        }
    }
}
