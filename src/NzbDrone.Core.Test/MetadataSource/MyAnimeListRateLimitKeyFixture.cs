using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.MetadataSource.MyAnimeList.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Wave 0 fixture — verifies that every outbound HttpRequest from the MyAnimeList
    // provider carries RateLimitKey == "myanimelist". Covers META-05 + threat T-DOS-01.
    // Plan 02-08 GREEN.
    [TestFixture]
    public class MyAnimeListRateLimitKeyFixture : CoreTest<MyAnimeListMetadataSource>
    {
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;

            Subject.Definition = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MyAnimeList",
                Implementation = "MyAnimeListMetadataSource",
                ConfigContract = nameof(MyAnimeListMetadataSourceSettings),
                Settings = new MyAnimeListMetadataSourceSettings
                {
                    BaseUrl = "https://api.myanimelist.net/v2",
                    ClientId = "test-client-id",
                    SourceKey = "myanimelist",
                },
                IsPrimary = false,
            };

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MalListEnvelope<MalMangaResource>>(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(req => _capturedRequest = req)
                  .Returns<HttpRequest>(req =>
                  {
                      var envelope = new MalListEnvelope<MalMangaResource>
                      {
                          Data = new System.Collections.Generic.List<MalNodeWrapper<MalMangaResource>>(),
                      };
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = Newtonsoft.Json.JsonConvert.SerializeObject(envelope);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MalListEnvelope<MalMangaResource>>(raw);
                  });
        }

        // request.HttpRequest.RateLimitKey must equal "myanimelist".
        [Test]
        public void Outbound_request_has_RateLimitKey_equal_myanimelist()
        {
            Subject.SearchForNewManga("test");

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("myanimelist");
        }
    }
}
