using System.IO;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayCapabilitiesProvider"/> (lands in Plan 37-01 Task 3 —
    /// this references the NOT-YET-BUILT production type and compiles GREEN only after Task 3;
    /// intentional contract-first ordering per the Wave-0 convention).
    ///
    /// Coverage (D-01 + A2):
    /// - cached_12h_unless_forceRefresh: two background reads → exactly ONE HTTP call;
    ///   a forceRefresh read → a SECOND call.
    /// - forceRefresh_removes_then_refetches: forceRefresh always bypasses the cache.
    /// - error_code_auth_throws_ApiKeyException: a gateway <c>error.code:"auth"</c> / HTTP 401
    ///   throws <see cref="ApiKeyException"/> (NOT a swallowed null — the kept exception ladder).
    /// </summary>
    [TestFixture]
    public class GatewayCapabilitiesProviderFixture : CoreTest<GatewayCapabilitiesProvider>
    {
        private string _capsJson;
        private GatewaySettings _settings;

        [SetUp]
        public void Setup()
        {
            _capsJson = File.ReadAllText("Files/Indexers/Gateway/caps.json");

            _settings = new GatewaySettings
            {
                BaseUrl = "http://localhost:9191",
                ApiKey = "test-api-key"
            };

            // Real cache so the 12h-TTL cache-hit behavior is genuinely exercised.
            Mocker.SetConstant<ICacheManager>(new CacheManager());
        }

        private void GivenHttpResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), content, statusCode));
        }

        [Test]
        public void cached_12h_unless_forceRefresh()
        {
            GivenHttpResponse(_capsJson);

            Subject.GetCapabilities(_settings);
            Subject.GetCapabilities(_settings);

            // Two background reads hit the cache → exactly ONE HTTP call.
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Once());

            Subject.GetCapabilities(_settings, forceRefresh: true);

            // forceRefresh bypasses the cache → a SECOND HTTP call.
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void forceRefresh_removes_then_refetches()
        {
            GivenHttpResponse(_capsJson);

            Subject.GetCapabilities(_settings);
            Subject.GetCapabilities(_settings, forceRefresh: true);
            Subject.GetCapabilities(_settings, forceRefresh: true);

            // Every forceRefresh issues a fresh HTTP call (3 calls total).
            Mocker.GetMock<IHttpClient>().Verify(c => c.Get(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Test]
        public void error_code_auth_throws_ApiKeyException()
        {
            GivenHttpResponse("{\"error\":{\"code\":\"auth\",\"message\":\"invalid key\"}}", HttpStatusCode.Unauthorized);

            var act = () => Subject.GetCapabilities(_settings);

            act.Should().Throw<ApiKeyException>();

            // The provider logs the rejecting host (never the api key) at Warn level.
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
