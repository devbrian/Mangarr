using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 — per-page HTTP GET surface consumed by plan 04-03's
    /// <c>ChapterDownloadService</c>. MUST set <c>request.RateLimitKey = aggregator.SourceKey</c>
    /// before every GET (Pitfall 1 mitigation; F-01 class regression guard).
    ///
    /// 403/410 responses raise <see cref="ManifestExpiredException"/> — caller orchestrates the
    /// D-03 single re-fetch + retry path OUTSIDE Polly. 5xx + RequestTimeout are retried via
    /// the internal Polly pipeline (MaxRetryAttempts=3).
    /// </summary>
    public interface IChapterPageFetcher
    {
        Task<byte[]> FetchPageBytesAsync(IHttpAggregator aggregator, ReleaseInfo release, ChapterPage page, CancellationToken ct);
    }
}
