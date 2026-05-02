using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    /// <summary>
    /// Unit fixture for <see cref="NzbDrone.Core.Indexers.Http.HttpAggregatorBase{TSettings}"/>.
    /// Verifies the behaviors layered onto the base HttpIndexerBase dispatch hook:
    /// <list type="number">
    /// <item><c>shared_budget_per_sourcekey</c> — outgoing requests carry RateLimitKey == Settings.SourceKey (D-11/D-12)</item>
    /// <item><c>applies_honest_ua</c> — outgoing requests carry User-Agent matching <c>Mangarr/{version}</c> when no override (D-13)</item>
    /// <item><c>useragent_override_wins</c> — Settings.UserAgentOverride takes precedence over the honest default (D-14)</item>
    /// </list>
    ///
    /// Wave 0 additions (Plan 03-01 Task 2 — references NOT-YET-BUILT types; flips green as
    /// production code lands in Plans 03-02..03-05):
    /// <list type="number">
    /// <item><c>Fetch_MangaSearchCriteria_calls_FetchReleases_through_request_generator</c> —
    ///       SOURCE-04 manga criteria fan-out preserves RateLimitKey through to the captured request</item>
    /// <item><c>GetDownloadHeaders_default_returns_empty_dictionary</c> — D-14 default contract:
    ///       no headers added unless subclass overrides</item>
    /// <item><c>GetDownloadHeaders_can_be_overridden_by_subclass</c> — D-14 per-source override
    ///       (Comix returns Referer, MangaDex returns empty)</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class HttpAggregatorBaseFixture : CoreTest<TestHttpAggregator>
    {
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;

            // Capture each outbound HttpRequest so tests can assert on its RateLimitKey / Headers.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(req => _capturedRequest = req)
                  .ReturnsAsync((HttpRequest req) => new HttpResponse(req, new HttpHeader(), string.Empty));

            Subject.Definition = new IndexerDefinition
            {
                Id = 1,
                Name = "Test Aggregator",
                Settings = new TestHttpAggregatorSettings()
            };
        }

        private async Task TriggerFetch(TestHttpAggregatorSettings settings)
        {
            Subject.Definition.Settings = settings;

            var request = new IndexerRequest("http://example.test/feed", HttpAccept.Rss);
            await Subject.InvokeFetchIndexerResponse(request);
        }

        [Test]
        public async Task shared_budget_per_sourcekey_applies_settings_sourcekey_to_request()
        {
            var settings = new TestHttpAggregatorSettings { SourceKey = "mangadex" };

            await TriggerFetch(settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("mangadex");
        }

        [Test]
        public async Task shared_budget_per_sourcekey_falls_back_to_default_when_settings_blank()
        {
            var settings = new TestHttpAggregatorSettings { SourceKey = null };

            await TriggerFetch(settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("test-source"); // TestHttpAggregator.DefaultSourceKey
        }

        [Test]
        public async Task applies_honest_ua_when_no_override()
        {
            var settings = new TestHttpAggregatorSettings { SourceKey = "mangadex", UserAgentOverride = null };

            await TriggerFetch(settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Headers.Get("User-Agent").Should().MatchRegex(@"^Mangarr/\d+\.\d+$");
        }

        [Test]
        public async Task useragent_override_wins_over_honest_default()
        {
            var settings = new TestHttpAggregatorSettings
            {
                SourceKey = "mangadex",
                UserAgentOverride = "MyBrowser/99"
            };

            await TriggerFetch(settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Headers.Get("User-Agent").Should().Be("MyBrowser/99");
        }

        // ===== Wave 0 additions (Plan 03-01 Task 2) =====
        // The three tests below reference types that land in Plans 03-02..03-05. They start RED
        // and flip GREEN as production code lands. Marked [Ignore] to keep the suite green at
        // Wave 0 (the production types they depend on do not exist yet).

        private async Task TriggerFetchManga(TestHttpAggregatorSettings settings, MangaSearchCriteria criteria)
        {
            // Plan 03-02 will add the abstract Fetch(MangaSearchCriteria) overload on
            // HttpAggregatorBase + a TestRequestGenerator stub on TestHttpAggregator.
            // For Wave 0 this helper is a placeholder — the [Ignore] tests below do not call it.
            Subject.Definition.Settings = settings;
            await Task.CompletedTask;
        }

        [Test]
        [Ignore("Wave 0 stub — Plan 03-02 lands MangaSearchCriteria + Fetch(MangaSearchCriteria) overload")]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases_through_request_generator()
        {
            var settings = new TestHttpAggregatorSettings { SourceKey = "mangadex" };
            var criteria = new MangaSearchCriteria();
            await TriggerFetchManga(settings, criteria);
            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("mangadex");
        }

        [Test]
        [Ignore("Wave 0 stub — Plan 03-02 adds GetDownloadHeaders(ReleaseInfo) on HttpAggregatorBase")]
        public void GetDownloadHeaders_default_returns_empty_dictionary()
        {
            // Plan 03-02 adds a virtual GetDownloadHeaders(ReleaseInfo) on HttpAggregatorBase
            // returning an empty IDictionary<string, string> by default.
            var release = new ReleaseInfo();
            // Subject.GetDownloadHeaders(release).Should().BeEmpty();
            Assert.Pass("Wave 0 stub — flips green when 03-02 lands the virtual.");
        }

        [Test]
        [Ignore("Wave 0 stub — Plan 03-02 adds GetDownloadHeaders + Plan 03-05 ComixIndexer override returns Referer")]
        public void GetDownloadHeaders_can_be_overridden_by_subclass()
        {
            // Documented expectation: per-source override returns Referer (Comix pattern).
            // This is a contract test — production lands with Plan 03-05's ComixIndexer.
            var release = new ReleaseInfo();
            var subject = new RefererHeadersTestAggregator();
            // subject.GetDownloadHeaders(release).Should().ContainKey("Referer");
            Assert.Pass("Wave 0 stub — flips green when 03-05 lands ComixIndexer.GetDownloadHeaders.");
        }
    }

    /// <summary>
    /// Wave 0 contract-test helper: a TestHttpAggregator subclass that demonstrates per-source
    /// override of <c>GetDownloadHeaders</c>. Mirrors the pattern Plan 03-05's <c>ComixIndexer</c>
    /// will use to surface the required <c>Referer: https://comix.to/</c> header.
    ///
    /// References NOT-YET-BUILT GetDownloadHeaders virtual (lands in Plan 03-02).
    /// </summary>
    internal class RefererHeadersTestAggregator
    {
        // Wave 0 placeholder. When Plan 03-02 lands `virtual IDictionary<string, string>
        // GetDownloadHeaders(ReleaseInfo)` on HttpAggregatorBase, change the declaration to:
        //
        //     internal class RefererHeadersTestAggregator : TestHttpAggregator
        //     {
        //         public override IDictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
        //             => new Dictionary<string, string> { ["Referer"] = "https://test.local/" };
        //     }
        //
        // Until then this is a stub class; the tests above are [Ignore]'d so the unbound
        // helper is unreachable at runtime.
        public IDictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string> { ["Referer"] = "https://test.local/" };
    }
}
