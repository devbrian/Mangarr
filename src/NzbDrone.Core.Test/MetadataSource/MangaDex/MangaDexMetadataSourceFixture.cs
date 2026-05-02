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

namespace NzbDrone.Core.Test.MetadataSource.MangaDex
{
    // GREEN — Plan 02-13 (Wave 7) flips Plan 02-06's Wave 0 scaffold to live assertions
    // against the production MangaDexMetadataSource. The four required test method names
    // (per the wave-3 contract + 02-VERIFICATION.md anti-pattern row) are load-bearing —
    // do NOT rename:
    //   * GetMangaInfo_extracts_links_al_via_int_TryParse
    //   * GetMangaInfo_extracts_links_mal_via_int_TryParse
    //   * Search_returns_results_from_canned_response
    //   * GetMangaInfo_throws_MangaNotFoundException_on_404
    //
    // Pitfall 7 anchor (Plan 02-01 acceptance): the cross-source link extraction must
    // call int.TryParse on the JSON `links.al` and `links.mal` STRING values (the
    // MangaDex API ships these as strings, not ints). Tests 1 and 2 below assert that.
    [TestFixture]
    public class MangaDexMetadataSourceFixture : CoreTest<MangaDexMetadataSource>
    {
        [SetUp]
        public void Setup()
        {
            // Bind a Definition with a concrete Settings instance so the lazy MangaDexApi
            // wrapper inside the production class can read Settings.BaseUrl (the property
            // is populated by ProviderFactory at runtime; in tests we set it explicitly).
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
        }

        // Verifies links.al → AniList id integer extraction via int.TryParse on the
        // STRING value MangaDex's API ships (Pitfall 7 anchor).
        [Test]
        public void GetMangaInfo_extracts_links_al_via_int_TryParse()
        {
            var resource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "11111111-1111-1111-1111-111111111111",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new System.Collections.Generic.Dictionary<string, string> { { "en", "Test Manga" } },
                        Links = new System.Collections.Generic.Dictionary<string, string> { { "al", "12345" } },
                    },
                },
            };
            SetupGetByIdMock(resource);
            SetupGetFeedMock();

            var result = Subject.GetMangaInfo("11111111-1111-1111-1111-111111111111");

            result.Item1.AniListId.Should().Be(12345);
        }

        // Verifies links.mal → MAL id integer extraction via int.TryParse (Pitfall 7 mirror).
        [Test]
        public void GetMangaInfo_extracts_links_mal_via_int_TryParse()
        {
            var resource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "22222222-2222-2222-2222-222222222222",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new System.Collections.Generic.Dictionary<string, string> { { "en", "Test Manga 2" } },
                        Links = new System.Collections.Generic.Dictionary<string, string> { { "mal", "67890" } },
                    },
                },
            };
            SetupGetByIdMock(resource);
            SetupGetFeedMock();

            var result = Subject.GetMangaInfo("22222222-2222-2222-2222-222222222222");

            result.Item1.MalId.Should().Be(67890);
        }

        [Test]
        public void Search_returns_results_from_canned_response()
        {
            var listResource = new MangaListResource
            {
                Data = new System.Collections.Generic.List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new System.Collections.Generic.Dictionary<string, string> { { "en", "Naruto" } },
                        },
                    },
                    new MangaDataItem
                    {
                        Id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new System.Collections.Generic.Dictionary<string, string> { { "en", "Bleach" } },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("naruto");

            results.Should().HaveCount(2);
            results[0].Title.Should().Be("Naruto");
        }

        [Test]
        public void GetMangaInfo_throws_MangaNotFoundException_on_404()
        {
            // MangaDexApi.GetById sets req.SuppressHttpError=true and inspects the response's
            // StatusCode for NotFound; it does NOT swallow a thrown HttpException. The mock
            // therefore RETURNS a 404 HttpResponse rather than throwing.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var raw = new HttpResponse(req, headers, "{}", HttpStatusCode.NotFound);
                      return new HttpResponse<MangaResource>(raw);
                  });

            System.Action act = () => Subject.GetMangaInfo("33333333-3333-3333-3333-333333333333");

            act.Should().Throw<MangaNotFoundException>();
        }

        // ---- Helpers ----

        private void SetupGetByIdMock(MangaResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(resource);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaResource>(raw);
                  });
        }

        private void SetupGetFeedMock()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<ChapterFeedResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(new ChapterFeedResource
                      {
                          Data = new System.Collections.Generic.List<ChapterFeedEntry>(),
                      });
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<ChapterFeedResource>(raw);
                  });
        }

        private void SetupSearchMock(MangaListResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaListResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(resource);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaListResource>(raw);
                  });
        }
    }
}
