using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    // GREEN — Plan 02-07 lands AniListMetadataSource. The four required test method names
    // (per the wave-3 contract) are load-bearing — do NOT rename:
    //   * GetMangaInfo_extracts_idMal_for_cross_source
    //   * GetMangaInfo_extracts_primary_author_from_staff_role_Story
    //   * GetMangaInfo_extracts_total_chapter_count_from_chapters_field
    //   * Search_returns_results_from_Page_media
    [TestFixture]
    public class AniListMetadataSourceFixture : CoreTest<AniListMetadataSource>
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

        // Stub IHttpClient.Post<AniListGraphQlResponse<MediaResponseShape>> with canned JSON.
        private void GivenMediaByIdResponse(string json)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<MediaResponseShape>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(req => new HttpResponse<AniListGraphQlResponse<MediaResponseShape>>(
                    new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, json)));
        }

        private void GivenMediaSearchResponse(string json)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post<AniListGraphQlResponse<PageResponseShape>>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(req => new HttpResponse<AniListGraphQlResponse<PageResponseShape>>(
                    new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, json)));
        }

        [Test]
        public void GetMangaInfo_extracts_idMal_for_cross_source()
        {
            // D-19 reverse-lookup: AniList exposes idMal on Media; resolver consumes it.
            const string json = @"{
                ""data"": {
                    ""Media"": {
                        ""id"": 30013,
                        ""idMal"": 11,
                        ""title"": { ""userPreferred"": ""One Piece"" },
                        ""chapters"": null,
                        ""startDate"": { ""year"": 1997 },
                        ""staff"": { ""edges"": [] }
                    }
                }
            }";
            GivenMediaByIdResponse(json);

            var result = Subject.GetMangaInfo("30013");

            result.Item1.AniListId.Should().Be(30013);
            result.Item1.MalId.Should().Be(11);
            result.Item2.Should().BeEmpty();
        }

        // D-21 multi-axis confirm input — primary author from staff entries with role="Story".
        [Test]
        public void GetMangaInfo_extracts_primary_author_from_staff_role_Story()
        {
            const string json = @"{
                ""data"": {
                    ""Media"": {
                        ""id"": 30013,
                        ""idMal"": 11,
                        ""title"": { ""userPreferred"": ""Demon Slayer"" },
                        ""chapters"": 205,
                        ""startDate"": { ""year"": 2016 },
                        ""staff"": {
                            ""edges"": [
                                { ""role"": ""Art"",        ""node"": { ""name"": { ""full"": ""Art Person"" } } },
                                { ""role"": ""Story"",      ""node"": { ""name"": { ""full"": ""Koyoharu Gotouge"" } } },
                                { ""role"": ""Translator"", ""node"": { ""name"": { ""full"": ""Translator A"" } } }
                            ]
                        }
                    }
                }
            }";
            GivenMediaByIdResponse(json);

            var result = Subject.GetMangaInfo("30013");

            result.Item1.PrimaryAuthor.Should().Be("Koyoharu Gotouge");
        }

        [Test]
        public void GetMangaInfo_extracts_total_chapter_count_from_chapters_field()
        {
            // D-21 axis: total-chapter-count from media.chapters scalar.
            const string json = @"{
                ""data"": {
                    ""Media"": {
                        ""id"": 30013,
                        ""title"": { ""userPreferred"": ""Naruto"" },
                        ""chapters"": 700,
                        ""startDate"": { ""year"": 1999 },
                        ""staff"": { ""edges"": [] }
                    }
                }
            }";
            GivenMediaByIdResponse(json);

            var result = Subject.GetMangaInfo("30013");

            result.Item1.TotalChapterCount.Should().Be(700);
        }

        [Test]
        public void Search_returns_results_from_Page_media()
        {
            // The MediaSearchQuery returns Page.media[]; the provider maps each into a Manga.
            const string json = @"{
                ""data"": {
                    ""Page"": {
                        ""pageInfo"": { ""currentPage"": 1, ""lastPage"": 1, ""hasNextPage"": false },
                        ""media"": [
                            { ""id"": 1, ""title"": { ""userPreferred"": ""Title A"" }, ""startDate"": { ""year"": 2010 }, ""staff"": { ""edges"": [] } },
                            { ""id"": 2, ""title"": { ""userPreferred"": ""Title B"" }, ""startDate"": { ""year"": 2011 }, ""staff"": { ""edges"": [] } }
                        ]
                    }
                }
            }";
            GivenMediaSearchResponse(json);

            var results = Subject.SearchForNewManga("anything");

            results.Should().HaveCount(2);
            results[0].AniListId.Should().Be(1);
            results[0].Title.Should().Be("Title A");
            results[1].AniListId.Should().Be(2);
        }
    }
}
