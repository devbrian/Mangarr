using System;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Indexers.Gateway
{
    public interface IGatewayCapabilitiesProvider
    {
        GatewayCapabilities GetCapabilities(GatewaySettings settings, bool forceRefresh = false);
    }

    /// <summary>
    /// Caches the gateway <c>/caps</c> document for 12h (D-01) keyed on the settings JSON, with a
    /// <c>forceRefresh</c> bypass (Test() + the source dropdown always refetch live) and a
    /// clear-on-edit handler (<see cref="ProviderUpdatedEvent{IIndexer}"/> invalidates the cache).
    /// A gateway <c>error.code</c> / non-2xx status translates into the kept FetchReleases
    /// exception ladder (A2) — never a swallowed null. DryIoc discovers the interface + the
    /// <c>IHandle&lt;&gt;</c> by convention; no manual registration.
    /// </summary>
    public class GatewayCapabilitiesProvider : IGatewayCapabilitiesProvider, IHandle<ProviderUpdatedEvent<IIndexer>>
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ICached<GatewayCapabilities> _cache;

        public GatewayCapabilitiesProvider(IHttpClient httpClient, ICacheManager cacheManager, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _cache = cacheManager.GetCache<GatewayCapabilities>(GetType());
        }

        public GatewayCapabilities GetCapabilities(GatewaySettings settings, bool forceRefresh = false)
        {
            // Key on the settings shape so distinct gateway configs cache independently
            // (NewznabCapabilitiesProvider idiom).
            var key = settings.ToJson();

            if (forceRefresh)
            {
                _cache.Remove(key);
            }

            // D-01: 12h TTL (NOT the 1h rolling cache, NOT Sonarr's 7-day Newznab caps).
            return _cache.Get(key, () => FetchCapabilities(settings), TimeSpan.FromHours(12));
        }

        public void Handle(ProviderUpdatedEvent<IIndexer> message)
        {
            // Settings-edit invalidation: drop the whole caps cache.
            _cache.Clear();
        }

        private GatewayCapabilities FetchCapabilities(GatewaySettings settings)
        {
            var request = new HttpRequestBuilder(settings.BaseUrl)
                .Resource("caps")
                .SetHeader("X-Api-Key", settings.ApiKey) // security V2/V6 — the value is NEVER logged.
                .Accept(HttpAccept.Json)
                .Build();

            request.SuppressHttpError = true;

            var response = _httpClient.Get(request);

            if (response.HasHttpError)
            {
                ThrowForError(response);
            }

            var capabilities = JsonConvert.DeserializeObject<GatewayCapabilities>(response.Content);

            if (capabilities == null)
            {
                throw new IndexerException(new IndexerResponse(new IndexerRequest(request), response),
                    "Gateway returned an empty /caps document");
            }

            return capabilities;
        }

        // error.code → the kept exception ladder (spike §9 / A2). auth|401 → ApiKeyException;
        // rate_limited|429 → TooManyRequestsException; else → IndexerException. Never null.
        private void ThrowForError(HttpResponse response)
        {
            var code = TryReadErrorCode(response.Content);

            if (code == "auth" || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Host only — never the api key.
                _logger.Warn("Gateway rejected the API key ({0})", response.Request.Url.Host);
                throw new ApiKeyException("Gateway authentication failed");
            }

            if (code == "rate_limited" || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new TooManyRequestsException(response.Request, response);
            }

            throw new IndexerException(
                new IndexerResponse(new IndexerRequest(response.Request), response),
                "Gateway /caps request failed with status {0}",
                response.StatusCode);
        }

        private string TryReadErrorCode(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<GatewayError>(content)?.Error?.Code;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
