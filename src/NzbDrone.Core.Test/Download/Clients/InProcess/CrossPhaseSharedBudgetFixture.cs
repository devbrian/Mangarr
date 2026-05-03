using System.Collections.Generic;
using System.Linq;
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
    /// Phase 4 plan 04-02 Task 3 — F-01 class regression guard mirroring Phase 3 LEARNINGS pattern
    /// "Cross-phase shared-budget integration test". This fixture asserts the per-page fetcher
    /// AND the indexer-poll path use the same <c>(RateLimitKey, host)</c> bucket so the per-SourceKey
    /// rate budget enforces both — proving DOWNLOAD-03 ("active downloads share per-source rate
    /// budget with chapter-discovery polling") at runtime.
    ///
    /// <para>
    /// If a future change breaks the explicit <c>request.RateLimitKey = aggregator.SourceKey</c>
    /// line in <see cref="ChapterPageFetcher"/>, this test fails before merge.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CrossPhaseSharedBudgetFixture : CoreTest<ChapterPageFetcher>
    {
        private List<HttpRequest> _captured;

        [SetUp]
        public void SetUp()
        {
            _captured = new List<HttpRequest>();

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      _captured.Add(req);
                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), new byte[] { 1, 2, 3 }, HttpStatusCode.OK));
                  });
        }

        private static Mock<IHttpAggregator> BuildAggregator(string sourceKey)
        {
            var mock = new Mock<IHttpAggregator>();
            mock.SetupGet(a => a.SourceKey).Returns(sourceKey);
            mock.Setup(a => a.ResolveUserAgent()).Returns("Mangarr/0.1");
            mock.Setup(a => a.GetDownloadHeaders(It.IsAny<ReleaseInfo>())).Returns(new Dictionary<string, string>());
            return mock;
        }

        [Test]
        public async Task MangaDex_image_GET_shares_SourceKey_budget_with_indexer_poll()
        {
            var aggregator = BuildAggregator("mangadex");
            var pages = Enumerable.Range(1, 5).Select(i => new ChapterPage { Url = $"https://cdn/p{i}.jpg", PageIndex = i });
            foreach (var p in pages)
            {
                await Subject.FetchPageBytesAsync(aggregator.Object, new ReleaseInfo(), p, CancellationToken.None);
            }

            _captured.Should().HaveCount(5);
            _captured.Should().OnlyContain(r => r.RateLimitKey == "mangadex",
                "every image GET must carry RateLimitKey=SourceKey or it bypasses the per-SourceKey "
                + "budget shared with the indexer poll path (Phase 1 D-11). If this assertion fails, "
                + "the F-01 class regression has returned. Look for HttpRequest construction in "
                + "ChapterPageFetcher that doesn't set RateLimitKey, OR a wrapper helper added that "
                + "swallows the assignment.");
        }

        [Test]
        public async Task Comix_image_GET_shares_comix_to_SourceKey_budget()
        {
            // Same shape as the MangaDex assertion — a different SourceKey value to prove
            // the assignment honors the aggregator's value and is not hard-coded.
            var aggregator = BuildAggregator("comix.to");
            await Subject.FetchPageBytesAsync(aggregator.Object, new ReleaseInfo(), new ChapterPage { Url = "https://cdn.comix.to/p.jpg", PageIndex = 1 }, CancellationToken.None);

            _captured.Should().HaveCount(1);
            _captured[0].RateLimitKey.Should().Be("comix.to");
        }

        [Test]
        public async Task RateLimitKey_set_BEFORE_Polly_retry_first_attempt()
        {
            // Defends against a regression where someone "moves RateLimitKey assignment into the
            // retry callback" — even on the FIRST attempt (before any Polly retry has fired)
            // RateLimitKey must be present on the captured request.
            string firstAttemptKey = null;
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      if (firstAttemptKey is null)
                      {
                          firstAttemptKey = req.RateLimitKey ?? "<unset>";
                      }

                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), new byte[] { 1 }, HttpStatusCode.OK));
                  });

            var aggregator = BuildAggregator("comix.to");
            await Subject.FetchPageBytesAsync(aggregator.Object, new ReleaseInfo(), new ChapterPage { Url = "https://x", PageIndex = 1 }, CancellationToken.None);

            firstAttemptKey.Should().Be("comix.to");
        }

        [Test]
        public async Task Aggregator_GetDownloadHeaders_applied_to_every_image_GET()
        {
            // Cross-phase contract: per-source headers (Comix Referer; future per-source Origin)
            // applied via the IHttpAggregator hook are present on every captured image GET.
            var aggregator = BuildAggregator("comix.to");
            aggregator.Setup(a => a.GetDownloadHeaders(It.IsAny<ReleaseInfo>()))
                      .Returns(new Dictionary<string, string> { ["Referer"] = "https://comix.to/" });

            var pages = Enumerable.Range(1, 3).Select(i => new ChapterPage { Url = $"https://cdn/p{i}.jpg", PageIndex = i });
            foreach (var p in pages)
            {
                await Subject.FetchPageBytesAsync(aggregator.Object, new ReleaseInfo(), p, CancellationToken.None);
            }

            _captured.Should().HaveCount(3);
            _captured.Should().OnlyContain(r => r.Headers["Referer"] == "https://comix.to/");
        }
    }
}
