using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MangaDex.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // GREEN — Plan 02-13 (Wave 7) flips Plan 02-06's Wave 0 scaffold to live assertion.
    // Verifies that every outbound HttpRequest from the MangaDex provider carries
    // RateLimitKey == "mangadex". Covers META-05 + threat T-DOS-01 (per-SourceKey
    // rate-limit budget enforcement). Test method name is load-bearing.
    [TestFixture]
    public class MangaDexRateLimitKeyFixture : CoreTest<MangaDexMetadataSource>
    {
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;

            // Bind a Definition with a concrete Settings instance so the lazy MangaDexApi
            // wrapper inside the production class can read Settings.BaseUrl. SourceKey
            // defaults to "mangadex" via DefaultSourceKey when settings.SourceKey is set.
            Subject.Definition = new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = "MangaDexMetadataSource",
                ConfigContract = nameof(MangaDexMetadataSourceSettings),
                Settings = new MangaDexMetadataSourceSettings
                {
                    BaseUrl = "https://api.mangadex.org",
                    SourceKey = "mangadex",
                },
                IsPrimary = true,
            };

            // Capture the outbound HttpRequest from the search dispatch path.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaListResource>(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(req => _capturedRequest = req)
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(new MangaListResource
                      {
                          Data = new System.Collections.Generic.List<MangaDataItem>(),
                      });
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaListResource>(raw);
                  });
        }

        // request.HttpRequest.RateLimitKey must equal "mangadex".
        [Test]
        public void Outbound_request_has_RateLimitKey_equal_mangadex()
        {
            Subject.SearchForNewManga("naruto");

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("mangadex");
        }
    }
}
