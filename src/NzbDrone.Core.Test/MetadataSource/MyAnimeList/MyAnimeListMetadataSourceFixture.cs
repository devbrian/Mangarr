using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.MetadataSource.MyAnimeList.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.MyAnimeList
{
    // Wave 0 fixture for MyAnimeListMetadataSource (D-24 — official MAL v2,
    // client-ID-only auth). Plan 02-08 GREEN — exercises the real provider via a mocked
    // IHttpClient, capturing each outbound HttpRequest so the three Wave 0 anchors can
    // assert on the literal headers and query parameters.
    //
    // Acceptance anchor (Plan 02-01): every outbound HttpRequest from this provider MUST
    // carry the literal header "X-MAL-CLIENT-ID" (D-24 + threat T-CRED-01).
    [TestFixture]
    public class MyAnimeListMetadataSourceFixture : CoreTest<MyAnimeListMetadataSource>
    {
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;

            // Bind a Definition with a concrete Settings instance carrying a non-empty
            // ClientId so MalApi can inject the X-MAL-CLIENT-ID header.
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
        }

        // The actual outbound HttpRequest must carry Headers["X-MAL-CLIENT-ID"].
        [Test]
        public void GetMangaInfo_includes_X_MAL_CLIENT_ID_header()
        {
            SetupGetByIdMock(BuildSampleManga());

            Subject.GetMangaInfo("12345");

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Headers["X-MAL-CLIENT-ID"].Should().Be("test-client-id");
        }

        [Test]
        public void GetMangaInfo_extracts_primary_author_from_authors_role_Story()
        {
            var resource = BuildSampleManga();
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("12345");

            // D-21 axis: primary author is the authors[] node whose Role == "Story".
            result.Item1.PrimaryAuthor.Should().Be("Hajime Isayama");
        }

        // MAL v2 sparse-by-default — must declare required fields per RESEARCH §Code Examples
        // Pattern 4 line 1120.
        [Test]
        public void Search_includes_required_fields_query_param()
        {
            SetupSearchMock();

            Subject.SearchForNewManga("attack on titan");

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Url.Query.Should().Contain("fields=");
            _capturedRequest.Url.Query.Should().Contain("num_chapters");
        }

        // ---- Helpers ----
        private void SetupGetByIdMock(MalMangaResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MalMangaResource>(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(req => _capturedRequest = req)
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = Newtonsoft.Json.JsonConvert.SerializeObject(resource);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MalMangaResource>(raw);
                  });
        }

        private void SetupSearchMock()
        {
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

        private static MalMangaResource BuildSampleManga()
        {
            return new MalMangaResource
            {
                Id = 12345,
                Title = "Attack on Titan",
                Synopsis = "Test synopsis",
                Status = "finished",
                Nsfw = "white",
                StartDate = "2009-09-09",
                NumChapters = 139,
                NumVolumes = 34,
                Authors = new System.Collections.Generic.List<MalAuthorEdge>
                {
                    new()
                    {
                        Role = "Story",
                        Node = new MalAuthorNode { Id = 1, FirstName = "Hajime", LastName = "Isayama" },
                    },
                },
                Genres = new System.Collections.Generic.List<MalGenre>
                {
                    new() { Id = 1, Name = "Action" },
                },
            };
        }
    }
}
