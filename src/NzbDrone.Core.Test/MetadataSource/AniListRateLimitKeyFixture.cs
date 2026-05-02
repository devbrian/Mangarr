using FluentAssertions;
using Moq;
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
    [TestFixture]
    public class AniListRateLimitKeyFixture : CoreTest<AniListMetadataSource>
    {
        [SetUp]
        public void Setup()
        {
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
                    new HttpResponse(req, new HttpHeader { ContentType = "application/json" },
                        @"{ ""data"": { ""Page"": { ""media"": [] } } }")));

            Subject.SearchForNewManga("anything");

            captured.Should().NotBeNull();
            captured.RateLimitKey.Should().Be("anilist");
        }
    }
}
