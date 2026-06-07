using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Verifies every outbound HttpRequest from the MangaBaka provider carries
    // RateLimitKey == "mangabaka" (D-10 — MangaBaka's rate budget is isolated from
    // MangaDex's "mangadex" budget). Mirror of MangaDexRateLimitKeyFixture.
    [TestFixture]
    public class MangaBakaRateLimitKeyFixture : CoreTest<MangaBakaMetadataSource>
    {
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;

            Subject.Definition = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaBaka",
                Implementation = "MangaBakaMetadataSource",
                ConfigContract = nameof(MangaBakaMetadataSourceSettings),
                Settings = new MangaBakaMetadataSourceSettings
                {
                    BaseUrl = "https://api.mangabaka.org",
                    SourceKey = "mangabaka",
                },
                IsPrimary = true,
            };

            // Capture the outbound HttpRequest from the search dispatch path.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaBakaSearchResource>(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(req => _capturedRequest = req)
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(new MangaBakaSearchResource
                      {
                          Data = new List<MangaBakaSeries>(),
                      });
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaBakaSearchResource>(raw);
                  });
        }

        [Test]
        public void Outbound_request_has_RateLimitKey_equal_mangabaka()
        {
            Subject.SearchForNewManga("test");

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("mangabaka");
        }
    }
}
