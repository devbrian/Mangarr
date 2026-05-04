using System;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Notifications.Kavita
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-16 + D-17 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/Plex/PlexTv/PlexTvProxy.cs (token cache).
    // Pattern 6 (RESEARCH §Don't Hand-Roll row 2): 30-min JWT TTL cache via Sonarr's
    // ICacheManager + 401 reauth-and-retry-once on actual expiry. Two-step Kavita auth flow:
    //   1. POST /api/Plugin/authenticate?apiKey=...&pluginName=Mangarr  -> { token: "<jwt>" }
    //   2. Subsequent calls add "Authorization: Bearer <jwt>"
    // Cache key isolates per (Url, ApiKey) so two Kavita providers configured against different
    // servers (or different API keys on the same server) do not share their JWT.
    public class KavitaProxy : IKavitaProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly ICached<string> _tokenCache;
        private readonly Logger _logger;

        public KavitaProxy(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _tokenCache = cacheManager.GetCache<string>(GetType(), "kavita-jwt");
            _logger = logger;
        }

        public void Scan(KavitaNotificationSettings settings)
        {
            // D-16 dispatch: per-library scan when LibraryId set, scan-all otherwise.
            var path = settings.LibraryId.HasValue
                ? $"api/Library/scan?libraryId={settings.LibraryId.Value}"
                : "api/Library/scan-all";

            ExecuteWithToken(settings, path, HttpMethod.Post);
            _logger.Debug("Kavita scan dispatched ({0})", path);
        }

        public void Test(KavitaNotificationSettings settings)
        {
            // Force a fresh authenticate to surface bad credentials immediately. The cached
            // token might still be valid for an old key — Test must validate the CURRENT key.
            _tokenCache.Remove(CacheKey(settings));
            _ = GetOrFetchToken(settings);
        }

        private HttpResponse ExecuteWithToken(KavitaNotificationSettings settings, string path, HttpMethod method)
        {
            var token = GetOrFetchToken(settings);

            try
            {
                return ExecuteOnce(settings, path, method, token);
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Pattern 6 reauth-retry-once. Cached JWT was rejected (real expiry, server
                // restart, or ApiKey rotation) — invalidate, refetch, retry. ONE retry only;
                // a second 401 propagates to the caller (NOT an infinite loop).
                _logger.Debug("Kavita 401; refreshing JWT and retrying once");
                _tokenCache.Remove(CacheKey(settings));
                token = GetOrFetchToken(settings);
                return ExecuteOnce(settings, path, method, token);
            }
        }

        private HttpResponse ExecuteOnce(KavitaNotificationSettings settings, string path, HttpMethod method, string token)
        {
            var url = $"{settings.Url.TrimEnd('/')}/{path}";

            var request = new HttpRequestBuilder(url)
                .Accept(HttpAccept.Json)
                .Build();

            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Method = method;
            request.SuppressHttpError = false;

            return _httpClient.Execute(request);
        }

        private string GetOrFetchToken(KavitaNotificationSettings settings)
        {
            return _tokenCache.Get(
                CacheKey(settings),
                () =>
                {
                    var authUrl = $"{settings.Url.TrimEnd('/')}/api/Plugin/authenticate?apiKey={WebUtility.UrlEncode(settings.ApiKey)}&pluginName=Mangarr";

                    var request = new HttpRequestBuilder(authUrl)
                        .Accept(HttpAccept.Json)
                        .Build();

                    request.Method = HttpMethod.Post;
                    request.SuppressHttpError = false;

                    var response = _httpClient.Execute(request);
                    var parsed = Json.Deserialize<KavitaAuthResponse>(response.Content);

                    if (parsed == null || string.IsNullOrEmpty(parsed.Token))
                    {
                        throw new Exception("Kavita authentication returned no token");
                    }

                    return parsed.Token;
                },
                TimeSpan.FromMinutes(30));
        }

        // Per-(Url, ApiKey) cache key so two providers against different servers OR different
        // API keys on the same server do not share a token.
        private static string CacheKey(KavitaNotificationSettings settings)
        {
            return $"{settings.Url}|{settings.ApiKey}";
        }
    }
}
