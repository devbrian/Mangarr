using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    /// <summary>
    /// Phase 26 Plan 26-02 (IL-07): Contract fixture for the extracted
    /// <see cref="AniListGraphQlTransport"/>. Verifies the 5 behaviors the metadata-source
    /// previously depended on inside its private <c>PostGraphQl&lt;T&gt;</c> body:
    ///   * happy path returns the parsed envelope
    ///   * 429 logs warning + rethrows (RESEARCH §Pitfall 6)
    ///   * RateLimitKey == "anilist"
    ///   * Content-Type: application/json
    ///   * HttpMethod.Post
    ///
    /// All 5 behaviors are load-bearing — the metadata-source fixture suite assumes them
    /// (existing AniListMetadataSource*Fixture tests were authored against this verbatim
    /// contract before extraction).
    /// </summary>
    [TestFixture]
    public class AniListGraphQlTransportFixture : CoreTest<AniListGraphQlTransport>
    {
        private const string ValidJsonBody = @"{""query"":""query { Media(id:1, type:MANGA) { id } }"",""variables"":null}";

        private const string ValidEnvelopeJson = @"{
            ""data"": {
                ""Media"": {
                    ""id"": 1,
                    ""title"": { ""userPreferred"": ""Cowboy Bebop"" }
                }
            }
        }";

        private HttpRequest CaptureRequestAndReturnEnvelope(string envelopeJson)
        {
            HttpRequest captured = null;

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<MediaResponseShape>>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => captured = req)
                .Returns<HttpRequest>(req => new HttpResponse<AniListGraphQlResponse<MediaResponseShape>>(
                    new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, envelopeJson)));

            // Trigger transport — the captured request is populated as a side effect.
            Subject.Post<MediaResponseShape>(ValidJsonBody);

            captured.Should().NotBeNull("transport must have called _httpClient.Post once");
            return captured;
        }

        [Test]
        public void post_happy_path_returns_envelope()
        {
            // Behavior 1: typed envelope is parsed + returned to the caller verbatim.
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<MediaResponseShape>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(req => new HttpResponse<AniListGraphQlResponse<MediaResponseShape>>(
                    new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, ValidEnvelopeJson)));

            var resp = Subject.Post<MediaResponseShape>(ValidJsonBody);

            resp.Should().NotBeNull();
            resp.Data.Should().NotBeNull();
            resp.Data.Media.Should().NotBeNull();
            resp.Data.Media.Id.Should().Be(1);
            resp.Data.Media.Title.UserPreferred.Should().Be("Cowboy Bebop");
        }

        [Test]
        public void post_on_429_logs_warn_and_rethrows()
        {
            // Behavior 2 (RESEARCH §Pitfall 6 verbatim): on HttpException with TooManyRequests
            // the transport logs a warning then rethrows. Never silently absorbs.
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<MediaResponseShape>>(It.IsAny<HttpRequest>()))
                .Throws<HttpException>(() =>
                {
                    var req = new HttpRequest(AniListMangaApi.GraphQlEndpoint);
                    var resp = new HttpResponse(req, new HttpHeader(), string.Empty, HttpStatusCode.TooManyRequests);
                    throw new HttpException(req, resp);
                });

            Action act = () => Subject.Post<MediaResponseShape>(ValidJsonBody);

            // Rethrow assertion — transport propagates the HttpException so the caller sees it.
            act.Should().Throw<HttpException>().Where(e => e.Response.StatusCode == HttpStatusCode.TooManyRequests);

            // Log assertion — LoggingTest base treats unexpected Warns as test failures, so the
            // intentional 429 warning must be acknowledged.
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void post_sets_ratelimit_key_to_anilist()
        {
            // Behavior 3: RateLimitKey must equal "anilist" so the request flows through the
            // 30 req/min AniList budget (matches AniListMetadataSource.DefaultSourceKey).
            var captured = CaptureRequestAndReturnEnvelope(ValidEnvelopeJson);
            captured.RateLimitKey.Should().Be("anilist");
        }

        [Test]
        public void post_sets_content_type_json()
        {
            // Behavior 4: Content-Type header is "application/json" — required by AniList GraphQL.
            var captured = CaptureRequestAndReturnEnvelope(ValidEnvelopeJson);
            captured.Headers["Content-Type"].Should().Be("application/json");
        }

        [Test]
        public void post_uses_http_method_post()
        {
            // Behavior 5: HttpMethod.Post — AniList GraphQL is POST-only.
            var captured = CaptureRequestAndReturnEnvelope(ValidEnvelopeJson);
            captured.Method.Should().Be(HttpMethod.Post);
        }
    }
}
