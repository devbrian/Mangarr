using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.MangaBaka
{
    /// <summary>
    /// Behavioral proof for the Phase 42 browse surface on <see cref="MangaBakaApi"/>:
    /// <c>Browse</c> / <c>GetGenres</c> / <c>GetTags</c>.
    ///
    /// <para>
    /// Constructs the API wrapper DIRECTLY (it is not DI-registered) with a mocked
    /// <c>IHttpClient</c>, captures the issued <see cref="HttpRequest"/>, and asserts:
    /// (1) repeated facets are emitted one-per-value, NEVER comma-joined (RESEARCH Pitfall 2 / A2);
    /// (2) tag facets bind the integer id (D-10); (3) Browse stamps the shared "mangabaka" 3s
    /// rate-limit bucket and GetGenres/GetTags the 0.5s lookup bucket (T-42-01-RATE).
    /// </para>
    /// </summary>
    [TestFixture]
    public class MangaBakaApiBrowseFixture : CoreTest
    {
        private Mock<IHttpClient> _httpClient;
        private MangaBakaApi _api;
        private HttpRequest _captured;

        [SetUp]
        public void Setup()
        {
            _captured = null;
            _httpClient = new Mock<IHttpClient>();
            _api = new MangaBakaApi(
                _httpClient.Object,
                "https://api.mangabaka.org",
                () => "Mangarr/test",
                "mangabaka");
        }

        private void SetupSearchMock()
        {
            _httpClient
                .Setup(c => c.Get<MangaBakaSearchResource>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => _captured = req)
                .Returns<HttpRequest>(req =>
                {
                    var headers = new HttpHeader { ContentType = "application/json" };
                    var body = JsonConvert.SerializeObject(new MangaBakaSearchResource
                    {
                        Data = new List<MangaBakaSeries>(),
                    });
                    return new HttpResponse<MangaBakaSearchResource>(
                        new HttpResponse(req, headers, body, HttpStatusCode.OK));
                });
        }

        [Test]
        public void Browse_builds_repeated_facet_params_and_stamps_3s_mangabaka_bucket()
        {
            SetupSearchMock();

            var filter = new DiscoveryFilter
            {
                Genre = new List<string> { "action", "shounen" },
                GenreNot = new List<string> { "boys_love" },
                Tag = new List<int> { 363 },
                TagMode = "or",
            };

            _api.Browse(filter, page: 1, limit: 24);

            _captured.Should().NotBeNull();
            var url = _captured.Url.ToString();

            // Repeated facets, one per value — NEVER comma-joined (Pitfall 2).
            url.Should().Contain("genre=action");
            url.Should().Contain("genre=shounen");
            url.Should().Contain("genre_not=boys_love");

            // Tag binds the integer id (D-10), and tag_mode is forwarded when a tag is present.
            url.Should().Contain("tag=363");
            url.Should().Contain("tag_mode=or");

            // Scalars.
            url.Should().Contain("page=1");
            url.Should().Contain("limit=24");

            // Shared 3s "mangabaka" rate-limit bucket (T-42-01-RATE).
            _captured.RateLimitKey.Should().Be("mangabaka");
            _captured.RateLimit.Should().Be(TimeSpan.FromSeconds(3));
        }

        [Test]
        public void GetGenres_stamps_half_second_lookup_bucket()
        {
            HttpRequest captured = null;
            _httpClient
                .Setup(c => c.Get<MangaBakaGenreListResource>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => captured = req)
                .Returns<HttpRequest>(req =>
                {
                    var headers = new HttpHeader { ContentType = "application/json" };
                    var body = JsonConvert.SerializeObject(new MangaBakaGenreListResource
                    {
                        Data = new List<MangaBakaGenre>(),
                    });
                    return new HttpResponse<MangaBakaGenreListResource>(
                        new HttpResponse(req, headers, body, HttpStatusCode.OK));
                });

            _api.GetGenres();

            captured.Should().NotBeNull();
            captured.Url.ToString().Should().Contain("/v1/genres");
            captured.RateLimitKey.Should().Be("mangabaka");
            captured.RateLimit.Should().Be(TimeSpan.FromSeconds(0.5));
        }

        [Test]
        public void GetTags_stamps_half_second_lookup_bucket()
        {
            HttpRequest captured = null;
            _httpClient
                .Setup(c => c.Get<MangaBakaTagListResource>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => captured = req)
                .Returns<HttpRequest>(req =>
                {
                    var headers = new HttpHeader { ContentType = "application/json" };
                    var body = JsonConvert.SerializeObject(new MangaBakaTagListResource
                    {
                        Data = new List<MangaBakaTag>(),
                    });
                    return new HttpResponse<MangaBakaTagListResource>(
                        new HttpResponse(req, headers, body, HttpStatusCode.OK));
                });

            _api.GetTags();

            captured.Should().NotBeNull();
            captured.Url.ToString().Should().Contain("/v1/tags");
            captured.RateLimitKey.Should().Be("mangabaka");
            captured.RateLimit.Should().Be(TimeSpan.FromSeconds(0.5));
        }
    }
}
