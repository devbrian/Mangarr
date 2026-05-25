using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Cloudflare;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Cloudflare
{
    /// <summary>
    /// Phase 33.2 D-09 reusability proof. A throwaway FAKE plain-IHttpClient-style
    /// consumer (<see cref="FakeClearedHttpConsumer"/>) depends on ONLY
    /// <see cref="ICloudflareClearanceService"/>, builds an
    /// <see cref="NzbDrone.Common.Http.HttpRequest"/>, and attaches BOTH the User-Agent
    /// and the <c>Cookie: cf_clearance=...</c> header atomically — the way a future
    /// browser-less HTML aggregator indexer (MangaFire/MangaPark) would. This file
    /// references NO indexer-specific signer type — that is the structural proof the
    /// clearance seam is generic (the Phase 17 manga-only signer rule governs the SIGNING
    /// axis only; CF clearance is a separate axis that IS allowed to generalize). The
    /// <c>grep -c</c> gate on the forbidden substring must return 0 for this file.
    /// </summary>
    [TestFixture]
    public class FakeHttpAggregatorClearanceConsumerFixture : CoreTest
    {
        /// <summary>
        /// In-test stand-in for a future generic HTML-aggregator indexer. Takes ONLY the
        /// generic clearance seam — proving no indexer-specific signer type is required to
        /// consume clearance.
        /// </summary>
        private sealed class FakeClearedHttpConsumer
        {
            private readonly ICloudflareClearanceService _clearance;

            public FakeClearedHttpConsumer(ICloudflareClearanceService clearance)
            {
                _clearance = clearance;
            }

            public async Task<HttpRequest> BuildRequestAsync(string url, CancellationToken ct)
            {
                var c = await _clearance.GetClearanceAsync(url, ct);

                var req = new HttpRequest(url);
                req.Headers["User-Agent"] = c.UserAgent;
                req.Headers["Cookie"] = $"cf_clearance={c.CfClearanceCookie}";
                return req;
            }
        }

        [Test]
        public async Task consumer_should_attach_user_agent_and_cf_clearance_cookie_atomically()
        {
            var clearance = new CloudflareClearance(
                "the-cf-clearance-value",
                "Mozilla/5.0 (matched-UA)",
                ".mangafire.to",
                "/",
                true,
                true,
                DateTimeOffset.UtcNow.AddMinutes(20));

            var mock = new Mock<ICloudflareClearanceService>();
            mock.Setup(s => s.GetClearanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(clearance);

            var consumer = new FakeClearedHttpConsumer(mock.Object);

            var req = await consumer.BuildRequestAsync("https://mangafire.to/", CancellationToken.None);

            req.Headers["User-Agent"].Should().Be("Mozilla/5.0 (matched-UA)");
            req.Headers["Cookie"].Should().Be("cf_clearance=the-cf-clearance-value");
        }
    }
}
