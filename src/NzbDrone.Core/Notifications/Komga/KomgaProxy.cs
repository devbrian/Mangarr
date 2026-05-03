using System;
using System.Collections.Generic;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Notifications.Komga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-15 + D-17 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowserProxy.cs.
    // Pitfall 2 (HIGH-priority): only POST /api/v1/libraries/{id}/scan exists. No scan-all
    // endpoint per komga.org/docs/openapi/library-scan/. Validator enforces LibraryId; this
    // class is defensive against direct construction without validation.
    // Auth: Komga 1.20.0+ X-API-Key header (NOT Bearer / NOT Basic).
    public class KomgaProxy : IKomgaProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public KomgaProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void Scan(KomgaNotificationSettings settings)
        {
            if (!settings.LibraryId.HasValue || settings.LibraryId.Value <= 0)
            {
                // Defensive: validator should have caught this. Pitfall 2 mitigation.
                throw new InvalidOperationException("Komga LibraryId is required for scan");
            }

            var path = $"api/v1/libraries/{settings.LibraryId.Value}/scan";
            var request = BuildRequest(settings, path, HttpMethod.Post);

            _httpClient.Execute(request);
            _logger.Debug("Komga scan dispatched for library {0}", settings.LibraryId.Value);
        }

        public List<KomgaLibrary> GetLibraries(KomgaNotificationSettings settings)
        {
            var request = BuildRequest(settings, "api/v1/libraries", HttpMethod.Get);
            var response = _httpClient.Execute(request);

            return Json.Deserialize<List<KomgaLibrary>>(response.Content);
        }

        private HttpRequest BuildRequest(KomgaNotificationSettings settings, string path, HttpMethod method)
        {
            var baseUrl = settings.Url.TrimEnd('/');
            var url = $"{baseUrl}/{path}";

            var request = new HttpRequestBuilder(url)
                .Accept(HttpAccept.Json)
                .Build();

            // X-API-Key header per Komga 1.20.0+ auth contract. Never logged (PrivacyLevel.ApiKey
            // strips at REST serialization; structured log redaction at NLog layer).
            request.Headers.Add("X-API-Key", settings.ApiKey);
            request.Method = method;
            request.SuppressHttpError = false;

            return request;
        }
    }
}
