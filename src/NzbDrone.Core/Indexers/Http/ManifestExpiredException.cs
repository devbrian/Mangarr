using System;
using System.Net;

namespace NzbDrone.Core.Indexers.Http
{
    /// <summary>
    /// Phase 4 D-03 — signals the 403/410 → re-fetch flow. Deliberately NOT inheriting from
    /// <see cref="NzbDrone.Common.Http.HttpException"/> so the Polly per-page retry pipeline's
    /// <c>HttpException</c> predicate does NOT swallow it (Pitfall 2). The orchestrator
    /// <c>ChapterDownloadService</c> (plan 04-03) catches this exception, calls
    /// <see cref="HttpAggregatorBase{TSettings}.GetChapterPages"/> ONCE more, and retries the
    /// page once with the refreshed manifest URL. A second 403/410 → terminal failure.
    /// </summary>
    public sealed class ManifestExpiredException : Exception
    {
        public int PageIndex { get; }
        public HttpStatusCode StatusCode { get; }

        public ManifestExpiredException(int pageIndex, HttpStatusCode statusCode)
            : base($"Manifest token expired at page {pageIndex} (HTTP {(int)statusCode}); re-fetch required.")
        {
            PageIndex = pageIndex;
            StatusCode = statusCode;
        }
    }
}
