using System;
using System.Collections.Generic;
using System.Net;
using NzbDrone.Common.Http;
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
            ApplyHeaders(req);

            var resp = _httpClient.Get<MangaBakaSearchResource>(req);
            return resp.Resource?.Data ?? new List<MangaBakaSeries>();
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
            ApplyHeaders(req);

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
        /// ("mangabaka" — isolated from MangaDex's "mangadex" budget per D-10), the honest
        /// UA (resolved via the injected <see cref="Func{String}"/>), and a JSON Accept
        /// header. Copied verbatim from <c>MangaDexApi.ApplyHeaders</c>.
        /// </summary>
        private void ApplyHeaders(HttpRequest req)
        {
            req.RateLimitKey = _sourceKey;
            req.Headers["User-Agent"] = _userAgent();
            req.Headers["Accept"] = "application/json";
        }
    }
}
