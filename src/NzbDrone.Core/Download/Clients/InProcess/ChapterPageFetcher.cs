using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Parser.Model;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 — per-page HTTP GET with shared per-SourceKey budget + honest UA + Polly retry.
    ///
    /// <para>
    /// Pitfall 1 mitigation: every GET MUST set <c>request.RateLimitKey = aggregator.SourceKey</c>
    /// at the call site explicitly (no wrapper that could be bypassed). The
    /// <c>CrossPhaseSharedBudgetFixture</c> is the F-01 class regression guard that fails CI if
    /// this assignment is ever removed or moved into a callback.
    /// </para>
    ///
    /// <para>
    /// Pitfall 2 mitigation: 403/410 are NOT in the Polly predicate — they raise
    /// <see cref="ManifestExpiredException"/> for the D-03 re-fetch flow OUTSIDE Polly.
    /// </para>
    ///
    /// <para>
    /// Sonarr divergence: per-page retry uses 3 attempts (CONTEXT Discretion);
    /// <c>DownloadClientBase.RetryStrategy</c> uses 2 for whole-download retries. Single-page
    /// retry has lower blast radius than whole-download retry, and image fetches are more
    /// jitter-prone than orchestration calls.
    /// </para>
    /// </summary>
    public class ChapterPageFetcher : IChapterPageFetcher
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        // Sonarr divergence: per-page retry uses 3 attempts (CONTEXT Discretion); DownloadClientBase
        // uses 2 for whole-download retries. Same predicate shape (5xx + RequestTimeout +
        // transient HttpException) so the canonical retry semantics carry through.
        // 403/410 deliberately NOT handled — they bubble out as ManifestExpiredException for D-03.
        private static readonly ResiliencePipeline<HttpResponse> PerPageRetry =
            new ResiliencePipelineBuilder<HttpResponse>()
                .AddRetry(new RetryStrategyOptions<HttpResponse>
                {
                    ShouldHandle = static args => args.Outcome switch
                    {
                        { Result.HasHttpServerError: true } => PredicateResult.True(),
                        { Result.StatusCode: HttpStatusCode.RequestTimeout } => PredicateResult.True(),
                        { Exception: HttpException { Response.HasHttpServerError: true } } => PredicateResult.True(),
                        _ => PredicateResult.False()
                    },
                    Delay = TimeSpan.FromSeconds(2),
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                })
                .Build();

        public ChapterPageFetcher(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<byte[]> FetchPageBytesAsync(IHttpAggregator aggregator, ReleaseInfo release, ChapterPage page, CancellationToken ct)
        {
            var req = new HttpRequest(page.Url);

            // ── CRITICAL — Pitfall 1 / F-01 class regression guard ──────────────────────
            // These three lines MUST run for every image GET. The CrossPhaseSharedBudgetFixture
            // asserts RateLimitKey is set on every captured request. DO NOT replace with a helper
            // that could be bypassed by a future caller; explicit assignment at the call site is
            // the contract.
            req.RateLimitKey = aggregator.SourceKey;
            req.Headers["User-Agent"] = aggregator.ResolveUserAgent();
            foreach (var kv in aggregator.GetDownloadHeaders(release))
            {
                req.Headers[kv.Key] = kv.Value;
            }

            // Polly handles 5xx + RequestTimeout. 403/410 fall through to the post-await branch
            // where they raise ManifestExpiredException OUTSIDE Polly (Pitfall 2).
            var resp = await PerPageRetry.ExecuteAsync(async _ => await _httpClient.ExecuteAsync(req), ct);

            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.Gone)
            {
                _logger.Info("Page {0} returned {1}; signalling manifest re-fetch", page.PageIndex, resp.StatusCode);
                throw new ManifestExpiredException(page.PageIndex, resp.StatusCode);
            }

            if (resp.HasHttpError)
            {
                throw new HttpException(req, resp);
            }

            return resp.ResponseData;
        }
    }
}
