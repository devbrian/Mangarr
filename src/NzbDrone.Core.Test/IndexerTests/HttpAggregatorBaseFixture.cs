using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    /// <summary>
    /// Unit fixture for <see cref="NzbDrone.Core.Indexers.Http.HttpAggregatorBase{TSettings}"/>.
    /// Verifies the three behaviors layered onto the base HttpIndexerBase dispatch hook:
    /// <list type="number">
    /// <item><c>shared_budget_per_sourcekey</c> — outgoing requests carry RateLimitKey == Settings.SourceKey (D-11/D-12)</item>
    /// <item><c>applies_honest_ua</c> — outgoing requests carry User-Agent matching <c>Mangarr/{version}</c> when no override (D-13)</item>
    /// <item><c>useragent_override_wins</c> — Settings.UserAgentOverride takes precedence over the honest default (D-14)</item>
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
    }
}
