using System;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Clients.Gateway.Responses;

namespace NzbDrone.Core.Download.Clients.Gateway
{
    /// <summary>
    /// All gateway HTTP I/O — the proxy half of the SABnzbd three-file decomposition. Structural
    /// mirror of <c>origin/v5-develop:src/NzbDrone.Core/Download/Clients/Sabnzbd/SabnzbdProxy.cs</c>
    /// (<c>BuildRequest</c> → <c>ProcessRequest</c> → <c>CheckForError</c> ladder), adapted to the
    /// V2 <c>X-Api-Key</c> header auth and the frozen <c>manga-gateway.openapi.yaml</c> contract.
    ///
    /// <para>
    /// TWO deliberate divergences from the SAB template (both cited inline; the
    /// sonarr-consistency-audit reads these):
    /// </para>
    /// <list type="number">
    /// <item>Submit (GWDL-04 / Pitfall 1): a <c>POST /downloads</c> returning HTTP 400 carries a
    /// <see cref="GatewaySubmitResponse"/> body (NOT the <c>Error</c> envelope) — it is PARSED and
    /// RETURNED with <c>JobId == null</c>, not thrown as a transport error. Only 401/5xx take the
    /// exception ladder.</item>
    /// <item>RemoveJob: a <c>DELETE /downloads/{jobId}</c> returning HTTP 404 (already removed) is
    /// SWALLOWED as idempotent success — the monitor may call <c>RemoveItem</c> twice. SAB has no
    /// analog.</item>
    /// </list>
    /// </summary>
    public class GatewayDownloadProxy : IGatewayDownloadProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public GatewayDownloadProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public GatewaySubmitResponse Submit(GatewaySubmitRequest request, GatewayDownloadClientSettings settings)
        {
            var httpRequest = BuildRequest("downloads", settings).Post().Build();
            httpRequest.SetContent(request.ToJson());
            httpRequest.Headers.ContentType = "application/json";

            var response = Execute(httpRequest, settings, _httpClient.Post);

            // Sonarr divergence (provenance: Sabnzbd/SabnzbdProxy.cs ProcessRequest+CheckForError) —
            // DIVERGENCE 1 (GWDL-04 / Pitfall 1): SAB routes EVERY non-2xx uniformly through
            // CheckForError. The gateway's POST /downloads 400 instead carries a SubmitResponse body
            // (frozen OpenAPI /downloads.post.responses.400 = SubmitResponse). Parse the body as
            // SubmitResponse for BOTH 200 AND 400 and return it (JobId == null on rejection); the
            // CLIENT translates that to DownloadClientRejectedReleaseException. Only OTHER non-2xx
            // (401/5xx) take the exception ladder.
            if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest)
            {
                if (Json.TryDeserialize<GatewaySubmitResponse>(response.Content, out var parsed) && parsed != null)
                {
                    return parsed;
                }

                return new GatewaySubmitResponse();
            }

            CheckForError(response);

            // Unreachable in practice (CheckForError throws on every non-2xx that reaches here).
            return Json.Deserialize<GatewaySubmitResponse>(response.Content);
        }

        public GatewayJobList GetJobs(GatewayDownloadClientSettings settings)
        {
            var httpRequest = BuildRequest("downloads", settings).Build();

            var response = Execute(httpRequest, settings, _httpClient.Get);

            CheckForError(response);

            return Json.Deserialize<GatewayJobList>(response.Content) ?? new GatewayJobList();
        }

        public GatewayJob GetJob(string jobId, GatewayDownloadClientSettings settings)
        {
            var httpRequest = BuildRequest($"downloads/{jobId}", settings).Build();

            var response = Execute(httpRequest, settings, _httpClient.Get);

            CheckForError(response);

            return Json.Deserialize<GatewayJob>(response.Content);
        }

        public void RemoveJob(string jobId, bool deleteData, GatewayDownloadClientSettings settings)
        {
            var requestBuilder = BuildRequest($"downloads/{jobId}", settings);
            requestBuilder.AddQueryParam("deleteData", deleteData);

            var httpRequest = requestBuilder.Build();
            httpRequest.Method = HttpMethod.Delete;

            var response = Execute(httpRequest, settings, _httpClient.Execute);

            // Sonarr divergence (provenance: Sabnzbd/SabnzbdProxy.cs RemoveFromQueue/RemoveFromHistory) —
            // DIVERGENCE 2: SAB has no 404-idempotency. A DELETE /downloads/{jobId} returning 404
            // (already removed) is SWALLOWED here — the Phase-36 monitor may call RemoveItem twice.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Debug("Gateway job '{0}' already removed (404) — treating DELETE as idempotent success.", jobId);
                return;
            }

            CheckForError(response);
        }

        public GatewayStatusResponse GetStatus(GatewayDownloadClientSettings settings)
        {
            var httpRequest = BuildRequest("status", settings).Build();

            var response = Execute(httpRequest, settings, _httpClient.Get);

            CheckForError(response);

            return Json.Deserialize<GatewayStatusResponse>(response.Content) ?? new GatewayStatusResponse();
        }

        public string GetVersion(GatewayDownloadClientSettings settings)
        {
            var httpRequest = BuildRequest("version", settings).Build();

            var response = Execute(httpRequest, settings, _httpClient.Get);

            CheckForError(response);

            return Json.TryDeserialize<GatewayVersion>(response.Content, out var version) ? version?.Version : null;
        }

        // Mirror of SabnzbdProxy.BuildRequest — base URL via HttpRequestBuilder.BuildBaseUrl,
        // X-Api-Key header on EVERY request (V2 auth), JSON Accept. SuppressHttpError lets the
        // caller inspect StatusCode (the two divergences + the ladder) instead of HttpException.
        private HttpRequestBuilder BuildRequest(string resource, GatewayDownloadClientSettings settings)
        {
            var baseUrl = HttpRequestBuilder.BuildBaseUrl(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase);

            var requestBuilder = new HttpRequestBuilder(baseUrl)
                .Resource(resource)
                .SetHeader("X-Api-Key", settings.ApiKey) // security V2/V6 — value is NEVER logged
                .Accept(HttpAccept.Json);

            requestBuilder.SuppressHttpError = true;

            return requestBuilder;
        }

        // Mirror of SabnzbdProxy.ProcessRequest's try/catch ladder — connect/DNS/TLS failures map to
        // DownloadClientUnavailableException; a raw HttpException maps to DownloadClientException.
        // Status-based mapping (401/403/4xx/5xx) lives in CheckForError so the two divergences can
        // inspect StatusCode before it runs.
        private HttpResponse Execute(HttpRequest httpRequest, GatewayDownloadClientSettings settings, Func<HttpRequest, HttpResponse> send)
        {
            _logger.Debug("Gateway request: {0} {1}", httpRequest.Method, httpRequest.Url);

            try
            {
                return send(httpRequest);
            }
            catch (HttpException ex)
            {
                throw new DownloadClientException("Unable to connect to the Manga Gateway, {0}", ex, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadClientUnavailableException("Unable to connect to the Manga Gateway, {0}", ex, ex.Message);
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.TrustFailure)
                {
                    throw new DownloadClientUnavailableException("Unable to connect to the Manga Gateway, certificate validation failed.", ex);
                }

                throw new DownloadClientUnavailableException("Unable to connect to the Manga Gateway, {0}", ex, ex.Message);
            }
        }

        // Mirror of SabnzbdProxy.CheckForError, adapted to the gateway's status-coded auth/Error
        // shape: 401/403 → Authentication; any other non-2xx → DownloadClientException. The two
        // divergences (Submit-400, DELETE-404) short-circuit BEFORE this runs.
        private void CheckForError(HttpResponse response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Host only — never the api key.
                _logger.Warn("Gateway rejected the API key ({0})", response.Request?.Url.Host);
                throw new DownloadClientAuthenticationException("Manga Gateway authentication failed");
            }

            if (response.HasHttpError)
            {
                throw new DownloadClientException("Manga Gateway request failed with status {0}", response.StatusCode);
            }
        }
    }
}
