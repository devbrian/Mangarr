using System;
using System.Collections.Generic;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;

namespace NzbDrone.Core.MetadataSource.MangaBaka
{
    /// <summary>
    /// HTTP wrapper for the MangaBaka REST API. Near-verbatim mirror of
    /// <c>MangaDexApi</c> but configured for MangaBaka's <c>{status, data}</c> envelopes
    /// and its own rate-budget contract.
    ///
    /// Every outbound request is tagged with <c>RateLimitKey = SourceKey</c>
    /// ("mangabaka" by default — D-10 budget, distinct from MangaDex's "mangadex"
    /// 40 req/min) plus the honest <c>Mangarr/{version}</c> User-Agent (resolved via the
    /// injected <see cref="Func{String}"/> so per-instance <c>Settings.UserAgentOverride</c>
    /// can — when set — opt out) and a JSON Accept header.
    ///
    /// Endpoints per MangaBaka's API:
    ///   GET /v1/series/search?q=...   — search by query (Search)
    ///   GET /v1/series/{id}           — single record (GetById; throws MangaNotFoundException on 404)
    ///
    /// No Ping/GetFeed — MangaBaka has neither (it ships no chapter feed; chapters are
    /// synthesized 1..total_chapters per D-02).
    /// </summary>
    public class MangaBakaApi
    {
        private const string DefaultBase = "https://api.mangabaka.org";

        // Per-request spacing enforced via the shared RateLimitService (keyed on SourceKey).
        // Setting RateLimitKey alone does NOT throttle — HttpClient only invokes the limiter when
        // request.RateLimit != TimeSpan.Zero (HttpClient.cs). Without this, List Sync's
        // StageViaPrimaryResolution fires one Search per item back-to-back with zero spacing and
        // trips MangaBaka's 429 budget.
        //
        // Intervals sit a safety margin UNDER the documented budget (30 req/min search,
        // 120 req/min lookup). 2s search (== 30/min, exactly the documented ceiling) was observed
        // live to still trip the 429 at the ~30s mark of a real List Sync — MangaBaka's enforced
        // limit is at or just below the documented one. 3s (== 20/min) cleared it; the lookup
        // budget is far looser, so 0.5s (== 120/min) is left at the documented ceiling (no 429 was
        // ever observed on the by-id path).
        private static readonly TimeSpan SearchRateLimit = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan LookupRateLimit = TimeSpan.FromSeconds(0.5);

        private readonly IHttpClient _httpClient;
        private readonly Func<string> _userAgent;
        private readonly string _sourceKey;
        private readonly string _baseUrl;

        public MangaBakaApi(IHttpClient httpClient, string baseUrl, Func<string> userAgent, string sourceKey)
        {
            _httpClient = httpClient;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.TrimEnd('/');
            _userAgent = userAgent;
            _sourceKey = sourceKey;
        }

        /// <summary>
        /// GET /v1/series/search?q=... — title search. The search envelope wraps a LIST
        /// of series records in <c>data</c> (verified live).
        /// </summary>
        public List<MangaBakaSeries> Search(string query)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/v1/series/search")
                .AddQueryParam("q", query)
                .Build();
            ApplyHeaders(req, SearchRateLimit);

            var resp = _httpClient.Get<MangaBakaSearchResource>(req);
            return resp.Resource?.Data ?? new List<MangaBakaSeries>();
        }

        /// <summary>
        /// GET /v1/series/search with the full browse filter (DISC-02). Reuses the SAME search
        /// endpoint + envelope as <see cref="Search"/> but binds the filter facets instead of a
        /// free-text query.
        ///
        /// CRITICAL (RESEARCH Pitfall 2 / A2): repeated facets are emitted as ONE
        /// <c>AddQueryParam</c> call PER value (which APPENDS — verified HttpRequestBuilder.cs:314),
        /// yielding e.g. <c>genre=action&amp;genre=shounen</c>. They are NEVER comma-joined (a comma
        /// trips MangaBaka's HTTP 400). Tag facets bind to the integer tag id (D-10), not the name.
        ///
        /// The adult-content injection (<c>content_rating=safe&amp;suggestive</c>) is the SERVICE's
        /// job (42-02), NOT this method — Browse only encodes the typed values it is handed
        /// (T-42-01-SSRF: fixed base host, no raw string concatenation).
        ///
        /// Stamps the shared 3s "mangabaka" rate-limit bucket via <see cref="ApplyHeaders"/>
        /// (T-42-01-RATE) — no new HTTP path, no <c>Thread.Sleep</c>.
        /// </summary>
        public MangaBakaSearchResource Browse(DiscoveryFilter filter, int page, int limit)
        {
            var builder = new HttpRequestBuilder($"{_baseUrl}/v1/series/search");

            foreach (var v in filter.Type)
            {
                builder.AddQueryParam("type", v);
            }

            foreach (var v in filter.TypeNot)
            {
                builder.AddQueryParam("type_not", v);
            }

            foreach (var v in filter.Genre)
            {
                builder.AddQueryParam("genre", v);
            }

            foreach (var v in filter.GenreNot)
            {
                builder.AddQueryParam("genre_not", v);
            }

            foreach (var v in filter.Status)
            {
                builder.AddQueryParam("status", v);
            }

            foreach (var v in filter.StatusNot)
            {
                builder.AddQueryParam("status_not", v);
            }

            foreach (var v in filter.ContentRating)
            {
                builder.AddQueryParam("content_rating", v);
            }

            // Tag facets bind to the integer tag id (D-10), serialized via ToString().
            foreach (var t in filter.Tag)
            {
                builder.AddQueryParam("tag", t.ToString());
            }

            foreach (var t in filter.TagNot)
            {
                builder.AddQueryParam("tag_not", t.ToString());
            }

            if ((filter.Tag.Count > 0 || filter.TagNot.Count > 0) && !string.IsNullOrWhiteSpace(filter.TagMode))
            {
                builder.AddQueryParam("tag_mode", filter.TagMode);
            }

            if (filter.YearLower.HasValue)
            {
                builder.AddQueryParam("year_lower", filter.YearLower.Value);
            }

            if (filter.YearUpper.HasValue)
            {
                builder.AddQueryParam("year_upper", filter.YearUpper.Value);
            }

            if (filter.RatingLower.HasValue)
            {
                builder.AddQueryParam("rating_lower", filter.RatingLower.Value);
            }

            if (filter.RatingUpper.HasValue)
            {
                builder.AddQueryParam("rating_upper", filter.RatingUpper.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                builder.AddQueryParam("sort_by", filter.SortBy);
            }

            builder.AddQueryParam("page", page);
            builder.AddQueryParam("limit", limit);

            var req = builder.Build();
            ApplyHeaders(req, SearchRateLimit);

            var resp = _httpClient.Get<MangaBakaSearchResource>(req);
            return resp.Resource;
        }

        /// <summary>
        /// GET /v1/genres — the genre option list (DISC-03). Lookup-budget (0.5s) bucket via
        /// <see cref="ApplyHeaders"/>. Returns the slim <c>{ label, value }</c> options (empty on a
        /// null envelope).
        /// </summary>
        public List<MangaBakaGenre> GetGenres()
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/v1/genres")
                .Build();
            ApplyHeaders(req, LookupRateLimit);

            var resp = _httpClient.Get<MangaBakaGenreListResource>(req);
            return resp.Resource?.Data ?? new List<MangaBakaGenre>();
        }

        /// <summary>
        /// GET /v1/tags — the tag option list (DISC-03 / D-10). Lookup-budget (0.5s) bucket via
        /// <see cref="ApplyHeaders"/>. Returns the SLIM tag options (id-bound, no description; empty
        /// on a null envelope).
        /// </summary>
        public List<MangaBakaTag> GetTags()
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/v1/tags")
                .Build();
            ApplyHeaders(req, LookupRateLimit);

            var resp = _httpClient.Get<MangaBakaTagListResource>(req);
            return resp.Resource?.Data ?? new List<MangaBakaTag>();
        }

        /// <summary>
        /// GET /v1/series/{id} — single record. Throws <see cref="MangaNotFoundException"/>
        /// on upstream 404 (capability parity with <c>MangaDexApi.GetById</c>); reuses the
        /// EXISTING exception type (no MangaBaka-specific exception).
        /// </summary>
        public MangaBakaSeries GetById(int id)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/v1/series/{id}")
                .Build();
            req.SuppressHttpError = true;       // we handle the 404 explicitly below
            ApplyHeaders(req, LookupRateLimit);

            var resp = _httpClient.Get<MangaBakaSeriesResource>(req);
            if (resp.HasHttpError)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new MangaNotFoundException(id.ToString());
                }

                throw new HttpException(req, resp);
            }

            return resp.Resource?.Data;
        }

        /// <summary>
        /// Stamp every outbound request with the SourceKey rate-limit budget tag
        /// ("mangabaka" — isolated from MangaDex's "mangadex" budget per D-10), the per-request
        /// spacing interval that actually engages the RateLimitService, the honest UA (resolved
        /// via the injected <see cref="Func{String}"/>), and a JSON Accept header.
        /// </summary>
        private void ApplyHeaders(HttpRequest req, TimeSpan rateLimit)
        {
            req.RateLimitKey = _sourceKey;
            req.RateLimit = rateLimit;
            req.Headers["User-Agent"] = _userAgent();
            req.Headers["Accept"] = "application/json";
        }
    }
}
