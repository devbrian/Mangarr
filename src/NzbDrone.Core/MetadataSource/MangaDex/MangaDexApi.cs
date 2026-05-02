using System;
using System.Collections.Generic;
using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.MetadataSource.MangaDex.Resource;

namespace NzbDrone.Core.MetadataSource.MangaDex
{
    /// <summary>
    /// HTTP wrapper for the MangaDex REST API. Sibling to <c>SkyHookProxy</c> (the
    /// only existing metadata-source HTTP wrapper) but configured for the manga domain
    /// and the Phase 1 SourceKey rate-budget contract.
    ///
    /// Every outbound request is tagged with <c>RateLimitKey = SourceKey</c>
    /// ("mangadex" by default — D-22 budget) plus the honest <c>Mangarr/{version}</c>
    /// User-Agent (Phase 1 D-13/D-14, MangaDex ToS). The constructor takes a
    /// <see cref="Func{String}"/> for the UA so the caller can short-circuit
    /// <c>Settings.UserAgentOverride</c> through <c>HttpMetadataSourceBase.ResolveUserAgent</c>.
    ///
    /// Endpoints per <c>https://api.mangadex.org/docs</c>:
    ///   GET /manga                       — search by title (Search; capped at limit=10)
    ///   GET /manga/{id}                  — single record (GetById; throws MangaNotFoundException on 404)
    ///   GET /manga/{id}/feed             — paginated chapter feed (GetFeed; limit=500 per page)
    /// </summary>
    public class MangaDexApi
    {
        private const string DefaultBase = "https://api.mangadex.org";

        // BL-07 cap: max pages we will pull from /manga/{id}/feed before bailing out.
        // pageSize=500 (MangaDex contract) * MaxPages=50 = 25,000 chapter rows, which
        // exceeds the longest known manga (One Piece ~1100 chapters × ~25 languages ×
        // multiple groups ≈ 70k entries — but the rate budget makes pulling that much
        // in one refresh undesirable anyway; we'd rather bail and surface a Warn so
        // the operator can investigate). The cap also defends against a server-side
        // pagination bug that would otherwise make the loop infinite.
        private const int MaxFeedPages = 50;

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(MangaDexApi));

        private readonly IHttpClient _httpClient;
        private readonly Func<string> _userAgent;
        private readonly string _sourceKey;
        private readonly string _baseUrl;

        public MangaDexApi(IHttpClient httpClient, string baseUrl, Func<string> userAgent, string sourceKey)
        {
            _httpClient = httpClient;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.TrimEnd('/');
            _userAgent = userAgent;
            _sourceKey = sourceKey;
        }

        /// <summary>
        /// GET /ping — MangaDex's connectivity check endpoint. Returns "pong" plain text.
        /// WR-17: used by <c>MangaDexMetadataSource.Test</c> as a lightweight Test path
        /// that does NOT count against the search rate budget the way <see cref="Search"/>
        /// does (one HTTP call vs. a full search query). Reference:
        /// https://api.mangadex.org/docs/02-mangadex/connectivity/
        /// </summary>
        public bool Ping()
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/ping").Build();
            req.SuppressHttpError = true;
            ApplyHeaders(req);

            var resp = _httpClient.Get(req);
            return !resp.HasHttpError;
        }

        /// <summary>
        /// GET /manga?title=...&amp;limit=10&amp;includes[]=cover_art&amp;includes[]=author
        /// Reference: https://api.mangadex.org/docs/redoc.html#tag/Manga/operation/get-search-manga
        /// </summary>
        public List<MangaDataItem> Search(string title)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/manga")
                .AddQueryParam("title", title)
                .AddQueryParam("limit", "10")
                .AddQueryParam("includes[]", "cover_art")
                .AddQueryParam("includes[]", "author")
                .Build();
            ApplyHeaders(req);

            var resp = _httpClient.Get<MangaListResource>(req);
            return resp.Resource?.Data ?? new List<MangaDataItem>();
        }

        /// <summary>
        /// GET /manga/{id}?includes[]=cover_art&amp;includes[]=author. Throws
        /// <see cref="MangaNotFoundException"/> on upstream 404 — mirrors
        /// <c>SkyHookProxy.GetSeriesInfo</c>'s <c>SeriesNotFoundException</c> shape.
        /// </summary>
        public MangaDataItem GetById(Guid mangaDexId)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/manga/{mangaDexId:D}")
                .AddQueryParam("includes[]", "cover_art")
                .AddQueryParam("includes[]", "author")
                .Build();
            req.SuppressHttpError = true;       // we handle the 404 explicitly below
            ApplyHeaders(req);

            var resp = _httpClient.Get<MangaResource>(req);
            if (resp.HasHttpError)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MangaNotFoundException(mangaDexId.ToString());
                }

                throw new HttpException(req, resp);
            }

            return resp.Resource?.Data;
        }

        /// <summary>
        /// GET /manga/{id}/feed?translatedLanguage[]=...&amp;limit=500&amp;offset=N
        /// Reference: https://api.mangadex.org/docs/04-chapter/feed/
        ///
        /// CRITICAL: this endpoint paginates at <c>limit=500</c> max; loop offset until
        /// a page returns &lt; pageSize entries. The pageSize=500 literal is the
        /// MangaDex contract — do not lower without changing per-call rate-budget math.
        /// </summary>
        public List<ChapterFeedEntry> GetFeed(Guid mangaDexId, string[] translatedLanguages = null)
        {
            translatedLanguages ??= Array.Empty<string>();
            var all = new List<ChapterFeedEntry>();
            var offset = 0;
            const int pageSize = 500;

            // BL-07 fix: bounded for-loop instead of `while (true)`. The MaxFeedPages
            // cap protects against (a) very long manga where pulling everything in one
            // refresh would exhaust the 40 req/min budget, and (b) any server-side
            // pagination bug that would otherwise make the loop infinite (e.g., the
            // server keeps returning a full page forever).
            for (var page = 0; page < MaxFeedPages; page++)
            {
                var rb = new HttpRequestBuilder($"{_baseUrl}/manga/{mangaDexId:D}/feed")
                    .AddQueryParam("limit", pageSize.ToString())
                    .AddQueryParam("offset", offset.ToString())
                    .AddQueryParam("order[chapter]", "asc")
                    .AddQueryParam("includes[]", "scanlation_group")
                    .AddQueryParam("contentRating[]", "safe")
                    .AddQueryParam("contentRating[]", "suggestive");

                foreach (var lang in translatedLanguages)
                {
                    rb.AddQueryParam("translatedLanguage[]", lang);
                }

                var req = rb.Build();
                ApplyHeaders(req);

                var resp = _httpClient.Get<ChapterFeedResource>(req);
                var batch = resp.Resource?.Data ?? new List<ChapterFeedEntry>();
                all.AddRange(batch);

                if (batch.Count < pageSize)
                {
                    return all;
                }

                offset += pageSize;
            }

            // Hit the cap — log Warn so operators see it. The partial page set is
            // still returned (better than throwing and losing the work).
            Logger.Warn(
                "MangaDex /manga/{0:D}/feed returned {1} pages without a partial page; bailing at cap (MaxFeedPages={2}) — manga may be unusually long or the upstream may be paginating incorrectly",
                mangaDexId,
                MaxFeedPages,
                MaxFeedPages);
            return all;
        }

        /// <summary>
        /// Stamp every outbound request with the SourceKey rate-limit budget tag,
        /// honest UA (resolved via the injected <see cref="Func{String}"/> so
        /// per-instance <c>Settings.UserAgentOverride</c> can — when set — opt out),
        /// and JSON Accept header. Mirrors <c>HttpMetadataSourceBase.BuildRequest</c>
        /// shape but applied after <see cref="HttpRequestBuilder"/> built the URL.
        /// </summary>
        private void ApplyHeaders(HttpRequest req)
        {
            req.RateLimitKey = _sourceKey;
            req.Headers["User-Agent"] = _userAgent();
            req.Headers["Accept"] = "application/json";
        }
    }
}
