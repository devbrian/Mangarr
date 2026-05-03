using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 plan 04-02 Task 3 — verifies the per-page <see cref="ChapterPageFetcher"/> applies
    /// the F-01 class regression guards (Pitfall 1 / Pitfall 2) at the call site:
    ///
    /// <list type="number">
    /// <item>RateLimitKey set on every GET (per-SourceKey budget shared with indexer poll).</item>
    /// <item>Honest UA applied via <c>aggregator.ResolveUserAgent()</c>.</item>
    /// <item>Per-source headers applied via <c>aggregator.GetDownloadHeaders(release)</c>.</item>
    /// <item>403/410 → <see cref="ManifestExpiredException"/> (NOT HttpException — Polly skip).</item>
    /// <item>5xx → Polly retry up to 3 attempts.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ChapterPageFetcherFixture : CoreTest<ChapterPageFetcher>
    {
        private Mock<IHttpAggregator> _aggregator;
        private List<HttpRequest> _captured;

        [SetUp]
        public void SetUp()
        {
            _captured = new List<HttpRequest>();

            _aggregator = new Mock<IHttpAggregator>();
            _aggregator.SetupGet(a => a.SourceKey).Returns("mangadex");
            _aggregator.Setup(a => a.ResolveUserAgent()).Returns("Mangarr/0.1");
            _aggregator.Setup(a => a.GetDownloadHeaders(It.IsAny<ReleaseInfo>())).Returns(new Dictionary<string, string>());

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      _captured.Add(req);
                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), new byte[] { 1, 2, 3 }, HttpStatusCode.OK));
                  });
        }

        private static ChapterPage Page(int idx = 1, string url = "https://cdn/1.jpg")
            => new ChapterPage { Url = url, PageIndex = idx };

        [Test]
        public async Task Sets_RateLimitKey_to_aggregator_SourceKey_on_every_GET()
        {
            // Pitfall 1 / F-01 class guard
            var rel = new ReleaseInfo { DownloadUrl = "https://x" };
            await Subject.FetchPageBytesAsync(_aggregator.Object, rel, Page(), CancellationToken.None);

            _captured.Should().HaveCount(1);
            _captured[0].RateLimitKey.Should().Be("mangadex");
        }

        [Test]
        public async Task Sets_honest_UserAgent_header()
        {
            await Subject.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(), CancellationToken.None);

            _captured[0].Headers["User-Agent"].Should().Be("Mangarr/0.1");
        }

        [Test]
        public async Task Applies_aggregator_GetDownloadHeaders()
        {
            _aggregator.Setup(a => a.GetDownloadHeaders(It.IsAny<ReleaseInfo>()))
                       .Returns(new Dictionary<string, string> { ["Referer"] = "https://x/" });

            await Subject.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(), CancellationToken.None);

            _captured[0].Headers["Referer"].Should().Be("https://x/");
        }

        [Test]
        public async Task Returns_response_data_on_success()
        {
            var bytes = await Subject.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(), CancellationToken.None);

            bytes.Should().Equal(new byte[] { 1, 2, 3 });
        }

        [Test]
        public async Task Forbidden_response_throws_ManifestExpiredException_NOT_HttpException()
        {
            // Pitfall 2 — 403 must escape Polly's predicate envelope as a distinct exception type
            // so plan 04-03's ChapterDownloadService can orchestrate the D-03 re-fetch.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                      Task.FromResult(new HttpResponse(req, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.Forbidden)));

            await Subject.Awaiting(s => s.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(5, "https://x"), CancellationToken.None))
                         .Should().ThrowAsync<ManifestExpiredException>()
                         .Where(ex => ex.PageIndex == 5 && ex.StatusCode == HttpStatusCode.Forbidden);
        }

        [Test]
        public async Task Gone_response_throws_ManifestExpiredException()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                      Task.FromResult(new HttpResponse(req, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.Gone)));

            await Subject.Awaiting(s => s.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(7, "https://x"), CancellationToken.None))
                         .Should().ThrowAsync<ManifestExpiredException>()
                         .Where(ex => ex.PageIndex == 7 && ex.StatusCode == HttpStatusCode.Gone);
        }

        [Test]
        public async Task ServerError_500_triggers_Polly_retry_up_to_3_attempts()
        {
            // Polly fires up to MaxRetryAttempts=3 retries on 5xx, so the mock observes 4 calls
            // (1 initial + 3 retries) before the pipeline gives up and the post-await branch
            // raises HttpException. Use mock counter, not a full ResponseFactory.
            var attempts = 0;
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      attempts++;
                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.InternalServerError));
                  });

            await Subject.Awaiting(s => s.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(), CancellationToken.None))
                         .Should().ThrowAsync<HttpException>();

            attempts.Should().BeGreaterOrEqualTo(4, "Polly fires 1 initial + 3 retries on 5xx (MaxRetryAttempts=3)");
        }

        [Test]
        public async Task NonRetryable_4xx_throws_HttpException_without_polly_retry()
        {
            // 400/404/etc. are not in the Polly predicate AND not 403/410, so they fall through
            // to the post-await branch as a generic HttpException.
            var attempts = 0;
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      attempts++;
                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.NotFound));
                  });

            await Subject.Awaiting(s => s.FetchPageBytesAsync(_aggregator.Object, new ReleaseInfo(), Page(), CancellationToken.None))
                         .Should().ThrowAsync<HttpException>();

            attempts.Should().Be(1, "404 is not retryable — Polly predicate is 5xx + RequestTimeout only");
        }
    }
}
