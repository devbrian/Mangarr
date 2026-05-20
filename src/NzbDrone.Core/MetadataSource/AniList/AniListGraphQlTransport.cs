using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.MetadataSource.AniList
{
    /// <summary>
    /// Shared AniList GraphQL HTTP transport per Phase 26 Plan 26-02 (IL-07).
    ///
    /// Verbatim extraction of the private <c>PostGraphQl&lt;T&gt;</c> method that previously
    /// lived at <see cref="AniListMetadataSource"/>:137-156. Lifted into a DI-injectable
    /// seam so both the existing metadata-source consumer (<see cref="AniListMetadataSource"/>)
    /// and the future Phase 27 <c>AniListImportList</c> consumer share a single GraphQL
    /// transport rather than duplicating the HTTP plumbing + 429 backoff branch.
    ///
    /// Behavior contract (verified by <c>AniListGraphQlTransportFixture</c>):
    ///   * <c>RateLimitKey == "anilist"</c> on every outbound request — matches
    ///     <see cref="AniListMetadataSource.DefaultSourceKey"/>; honors the 30 req/min
    ///     AniList budget per Phase 1 STACK research.
    ///   * <c>Content-Type: application/json</c> header + <c>HttpMethod.Post</c>.
    ///   * On <see cref="HttpStatusCode.TooManyRequests"/> (429): log warning + rethrow
    ///     so callers see the failure. Per RESEARCH §Pitfall 6 the rethrow is intentional
    ///     — never silently absorb. <c>IIndexerStatusService</c> integration is deferred
    ///     to Phase 3 indexer side (Phase 26 transport stays fire-once-per-add).
    /// </summary>
    public interface IAniListGraphQlTransport
    {
        AniListGraphQlResponse<T> Post<T>(string body);
    }

    /// <inheritdoc cref="IAniListGraphQlTransport"/>
    public class AniListGraphQlTransport : IAniListGraphQlTransport
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public AniListGraphQlTransport(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public AniListGraphQlResponse<T> Post<T>(string body)
        {
            var req = new HttpRequest(AniListMangaApi.GraphQlEndpoint)
            {
                Method = HttpMethod.Post,
                RateLimitKey = "anilist",
            };
            req.Headers["Content-Type"] = "application/json";
            req.SetContent(body);

            try
            {
                var resp = _httpClient.Post<AniListGraphQlResponse<T>>(req);
                return resp.Resource;
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Per RESEARCH §Pitfall 6: log warning. IIndexerStatusService integration deferred
                // to Phase 3 indexer side (Phase 2 metadata source is fire-once-per-add).
                _logger.Warn("AniList returned 429 Too Many Requests; honor Retry-After + back off");
                throw;
            }
        }
    }
}
