using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // GREEN — Plan 02-07 lands the RateLimitKey = "anilist" tag. Verifies that every outbound
    // HttpRequest from the AniList provider carries RateLimitKey == "anilist". Covers META-05 +
    // threat T-DOS-01. The literal "anilist" is the load-bearing wave-state contract — do NOT
    // rename.
    //
    // Phase 26 Plan 26-02 (IL-07): RateLimitKey wiring is now owned by AniListGraphQlTransport
    // (verified by AniListGraphQlTransportFixture.post_sets_ratelimit_key_to_anilist). This
    // fixture remains as an end-to-end gate that the metadata-source dispatches through the
    // transport (so the budget is honored) — wires a REAL transport instance over the mocked
    // IHttpClient rather than mocking the transport itself.
    [TestFixture]
    public class AniListRateLimitKeyFixture : CoreTest<AniListMetadataSource>
    {
        [SetUp]
        public void Setup()
        {
            // Wire a real AniListGraphQlTransport so the metadata-source's _transport.Post<>()
            // call flows through to the mocked IHttpClient — preserving the end-to-end
            // RateLimitKey wiring contract this fixture has always verified.
            Mocker.SetConstant<IAniListGraphQlTransport>(
                new AniListGraphQlTransport(Mocker.Resolve<IHttpClient>(), LogManager.GetCurrentClassLogger()));

            Subject.Definition = new MetadataSourceDefinition
            {
                Name = "AniList",
                Settings = new AniListMetadataSourceSettings(),
                IsPrimary = false,
            };
        }

        // request.HttpRequest.RateLimitKey must equal "anilist".
        [Test]
        public void Outbound_request_has_RateLimitKey_equal_anilist()
        {
            HttpRequest captured = null;

            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<PageResponseShape>>(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(req => captured = req)
                .Returns<HttpRequest>(req => new HttpResponse<AniListGraphQlResponse<PageResponseShape>>(
                    new HttpResponse(
                        req,
                        new HttpHeader { ContentType = "application/json" },
                        @"{ ""data"": { ""Page"": { ""media"": [] } } }")));

            Subject.SearchForNewManga("anything");

            captured.Should().NotBeNull();
            captured.RateLimitKey.Should().Be("anilist");
        }
    }
}
