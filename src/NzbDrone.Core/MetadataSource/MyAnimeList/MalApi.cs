using System;
using System.Collections.Generic;
using System.Net;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.MyAnimeList.Resource;

namespace NzbDrone.Core.MetadataSource.MyAnimeList
{
    /// <summary>
    /// MAL v2 HTTP client — wraps <c>https://api.myanimelist.net/v2/manga</c> search and
    /// detail endpoints. Per D-24 every outbound request carries the user-pasted
    /// X-MAL-CLIENT-ID header (client-ID-only auth, NOT OAuth). Per RESEARCH §Code Examples
    /// Pattern 4 line 1120 the <c>fields</c> query parameter is REQUIRED — MAL v2 returns a
    /// sparse default response without it.
    /// </summary>
    public class MalApi
    {
        private const string DefaultBase = "https://api.myanimelist.net/v2";

        // The 'fields' parameter is REQUIRED — MAL v2 returns SPARSE default response.
        // Reference: https://myanimelist.net/apiconfig/references/api/v2 (Manga endpoints)
        private const string MangaFields =
            "id,title,alternative_titles{synonyms,en,ja},main_picture{medium,large}," +
            "start_date,end_date,synopsis,mean,rank,popularity,nsfw,genres,status," +
            "num_volumes,num_chapters,authors{first_name,last_name},pictures";

        private readonly IHttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Func<string> _userAgent;
        private readonly Func<string> _clientId;        // user-pasted MAL API client ID per D-24
        private readonly string _sourceKey;

        public MalApi(
            IHttpClient httpClient,
            string baseUrl,
            Func<string> userAgent,
            Func<string> clientId,
            string sourceKey)
        {
            _httpClient = httpClient;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.TrimEnd('/');
            _userAgent = userAgent;
            _clientId = clientId;
            _sourceKey = sourceKey;
        }

        public List<MalMangaResource> Search(string query)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/manga")
                .AddQueryParam("q", query)
                .AddQueryParam("limit", "10")
                .AddQueryParam("fields", MangaFields)
                .Build();
            ApplyHeaders(req);
            var resp = _httpClient.Get<MalListEnvelope<MalMangaResource>>(req);
            var data = resp.Resource?.Data ?? new List<MalNodeWrapper<MalMangaResource>>();
            var result = new List<MalMangaResource>();
            foreach (var d in data)
            {
                if (d?.Node != null)
                {
                    result.Add(d.Node);
                }
            }

            return result;
        }

        public MalMangaResource GetById(int malId)
        {
            var req = new HttpRequestBuilder($"{_baseUrl}/manga/{malId}")
                .AddQueryParam("fields", MangaFields)
                .Build();
            ApplyHeaders(req);

            // Suppress the IHttpClient 404 throw so we can map it to MangaNotFoundException
            // (Sonarr's IHttpClient.Get<T> throws HttpException on HasHttpError unless this
            // flag is set — see SkyHookProxy.GetSeriesInfo precedent at line 51).
            req.SuppressHttpError = true;

            var resp = _httpClient.Get<MalMangaResource>(req);
            if (resp.HasHttpError && resp.StatusCode == HttpStatusCode.NotFound)
            {
                throw new MangaNotFoundException(malId.ToString());
            }

            if (resp.HasHttpError)
            {
                throw new HttpException(req, resp);
            }

            return resp.Resource;
        }

        private void ApplyHeaders(HttpRequest req)
        {
            // BL-08 fix: explicit guard so a null/empty ClientId surfaces as a clean
            // InvalidOperationException rather than NRE-ing inside the HttpRequest
            // header dictionary setter. Reachable from RefreshMangaService /
            // AddMangaService / MangaLookupController paths (NOT just Test) — a user
            // who promotes MAL to primary then clears ClientId in settings would
            // otherwise crash every refresh and lookup with an opaque NRE.
            var clientId = _clientId();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("MAL ClientId is not configured");
            }

            req.RateLimitKey = _sourceKey;
            req.Headers["User-Agent"] = _userAgent();
            req.Headers["X-MAL-CLIENT-ID"] = clientId;   // D-24 client-ID-only auth
            req.Headers["Accept"] = "application/json";
        }
    }
}
