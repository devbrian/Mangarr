using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.MangaBaka
{
    // Behavioral proof for MangaBakaMetadataSource (Plan 41-03 Task 2). Mirrors the MangaDex
    // fixture convention: inline Resource POCOs + mocked IHttpClient.Get<T> (NO JSON cassettes).
    // Covers the locked decisions: D-03 (direct anilist/mal id read), D-03a (MangaDexId null),
    // D-04 (synthesize-nothing on bad input), D-05 (MaxWholeCap clamp + Warn), D-06 (synthesized
    // row field shape), D-07 (empty cross-source lists), the 404 path, Search, and the D-09
    // canned-search Test().
    [TestFixture]
    public class MangaBakaMetadataSourceFixture : CoreTest<MangaBakaMetadataSource>
    {
        [SetUp]
        public void Setup()
        {
            // Bind a Definition with a concrete Settings instance so the lazy MangaBakaApi
            // wrapper inside the production class can read Settings.BaseUrl (populated by
            // ProviderFactory at runtime; set explicitly in tests).
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
        }

        // D-03: source.anilist.id is a RAW INTEGER on the MangaBaka record — read straight in
        // (no int.TryParse on a string), short-circuiting CrossSourceIdResolver.
        [Test]
        public void GetMangaInfo_reads_anilist_id_directly()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Test Manga",
                    Source = new MangaBakaSource { AniList = new MangaBakaSourceRef { Id = 105398 } },
                },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.AniListId.Should().Be(105398);
        }

        // debug `alt-title-collision-guard` (2026-06-19): junk placeholder titles are dropped at
        // the write gate (AddNormalized) so they never enter AlternativeTitles and become a
        // cross-title resolution key. Distinctive titles still flow through.
        [Test]
        public void GetMangaInfo_drops_junk_placeholder_alternative_titles()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Real Distinctive Title",
                    NativeTitle = "Unknown Title",   // junk placeholder — must be dropped
                    RomanizedTitle = "Untitled",     // junk placeholder — must be dropped
                },
            };
            SetupGetByIdMock(resource);

            var manga = Subject.GetMangaInfo("3397").Item1;

            manga.AlternativeTitles.Should().Contain("real distinctive title");
            manga.AlternativeTitles.Should().NotContain("unknown title");
            manga.AlternativeTitles.Should().NotContain("untitled");
        }

        // D-03: source.my_anime_list.id is a RAW INTEGER — read straight into MalId.
        [Test]
        public void GetMangaInfo_reads_mal_id_directly()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Test Manga",
                    Source = new MangaBakaSource { MyAnimeList = new MangaBakaSourceRef { Id = 121496 } },
                },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.MalId.Should().Be(121496);
        }

        // D-03a: a MangaBaka-sourced manga is NOT a MangaDex record — MangaDexId stays null.
        [Test]
        public void GetMangaInfo_leaves_MangaDexId_null()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Test Manga",
                    Source = new MangaBakaSource { AniList = new MangaBakaSourceRef { Id = 105398 } },
                },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.MangaDexId.Should().BeNull();
        }

        // D-03a: MangaBakaId is set from the record id.
        [Test]
        public void GetMangaInfo_sets_MangaBakaId_from_record_id()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries { Id = 3397, Title = "Test Manga" },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.MangaBakaId.Should().Be(3397);
        }

        // quick-260608-l2e: all five additional cross-source ids (kitsu/ann/shikimori as int,
        // anime_planet/manga_updates as string) map straight from the real series `source` block.
        // Reads the live captured payload (series_by_id_3397.json — Solo Leveling) through the
        // production GetMangaInfo path.
        [Test]
        public void GetMangaInfo_maps_all_cross_source_ids_from_real_payload()
        {
            var resource = JsonConvert.DeserializeObject<MangaBakaSeriesResource>(
                File.ReadAllText(Path.Combine("Files", "MetadataSource", "MangaBaka", "series_by_id_3397.json")));
            SetupGetByIdMock(resource);

            var manga = Subject.GetMangaInfo("3397").Item1;

            manga.KitsuId.Should().Be(54114);
            manga.AnimeNewsNetworkId.Should().Be(25998);
            manga.ShikimoriId.Should().Be(121496);
            manga.AnimePlanetId.Should().Be("solo-leveling");
            manga.MangaUpdatesId.Should().Be("6z1uqw7");
        }

        // Phase 41 fix-forward: MangaBaka ships the publication demographic INSIDE the
        // flat `genres` array (no dedicated field). MapManga extracts it so the
        // DemographicSpecification auto-tagging path works for MangaBaka-sourced manga
        // (parity with MangaDexMetadataSource.MapDemographic).
        [TestCase("shounen", MangaDemographic.Shonen)]
        [TestCase("shonen", MangaDemographic.Shonen)]
        [TestCase("shoujo", MangaDemographic.Shojo)]
        [TestCase("shojo", MangaDemographic.Shojo)]
        [TestCase("seinen", MangaDemographic.Seinen)]
        [TestCase("josei", MangaDemographic.Josei)]
        public void GetMangaInfo_maps_demographic_from_genres(string genre, MangaDemographic expected)
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Test Manga",
                    Genres = new List<string> { "action", "adventure", genre },
                },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.Demographic.Should().Be(expected);
        }

        // No demographic term among the genres → Demographic stays null (the
        // "not categorized" sentinel; Manga.Demographic is nullable).
        [Test]
        public void GetMangaInfo_leaves_demographic_null_when_no_demographic_genre()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 3397,
                    Title = "Test Manga",
                    Genres = new List<string> { "action", "adventure", "fantasy" },
                },
            };
            SetupGetByIdMock(resource);

            var result = Subject.GetMangaInfo("3397");

            result.Item1.Demographic.Should().BeNull();
        }

        // D-04: total_chapters="201" synthesizes a whole-number 1..201 catalog.
        [Test]
        public void GetMangaInfo_synthesizes_whole_chapters_from_total_chapters()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries { Id = 3397, Title = "Test Manga", TotalChapters = "201" },
            };
            SetupGetByIdMock(resource);

            var chapters = Subject.GetMangaInfo("3397").Item2.ToList();

            chapters.Should().HaveCount(201);
            chapters.First().ChapterNumber.Should().Be(1m);
            chapters.Last().ChapterNumber.Should().Be(201m);
        }

        // D-04: null / "" / "0" / non-integer total_chapters synthesizes NOTHING.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("0")]
        [TestCase("abc")]
        public void GetMangaInfo_synthesizes_nothing_on_bad_total_chapters(string totalChapters)
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries { Id = 3397, Title = "Test Manga", TotalChapters = totalChapters },
            };
            SetupGetByIdMock(resource);

            var chapters = Subject.GetMangaInfo("3397").Item2;

            chapters.Should().BeEmpty();
        }

        // D-05: total_chapters above MaxWholeCap clamps to 5000 rows and logs a Warn.
        [Test]
        public void GetMangaInfo_clamps_synthesis_at_MaxWholeCap_and_warns()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries { Id = 3397, Title = "Test Manga", TotalChapters = "9999" },
            };
            SetupGetByIdMock(resource);

            var chapters = Subject.GetMangaInfo("3397").Item2.ToList();

            chapters.Should().HaveCount(5000);
            ExceptionVerification.ExpectedWarns(1);
        }

        // D-06: synthesized row field shape — provenance ExternalId, Monitored=true, null
        // Title/FirstReleaseDate/VolumeNumber (mirrors MapChapter; refresh path does NOT
        // re-run the monitor policy layer).
        [Test]
        public void GetMangaInfo_synthesized_row_field_shape()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries { Id = 3397, Title = "Test Manga", TotalChapters = "3" },
            };
            SetupGetByIdMock(resource);

            var first = Subject.GetMangaInfo("3397").Item2.First();

            first.ExternalId.Should().Be("mangabaka:3397:c1");
            first.Monitored.Should().BeTrue();
            first.Title.Should().BeNull();
            first.FirstReleaseDate.Should().BeNull();
            first.VolumeNumber.Should().BeNull();
        }

        [Test]
        public void Search_returns_results_from_canned_response()
        {
            var searchResource = new MangaBakaSearchResource
            {
                Data = new List<MangaBakaSeries>
                {
                    new MangaBakaSeries { Id = 1, Title = "Naruto" },
                    new MangaBakaSeries { Id = 2, Title = "Bleach" },
                },
            };
            SetupSearchMock(searchResource);

            var results = Subject.SearchForNewManga("naruto");

            results.Should().HaveCount(2);
            results[0].Title.Should().Be("Naruto");
        }

        // Mangarr is a manga/manhwa/manhua manager, not a novel reader: MangaBaka `type: "novel"`
        // records (light/web novels) must be filtered out of search results so they never appear
        // in the Add picker or the rematch/relink candidate set. Comic types + "other" pass through.
        [Test]
        public void Search_excludes_novel_type_records()
        {
            var searchResource = new MangaBakaSearchResource
            {
                Data = new List<MangaBakaSeries>
                {
                    new MangaBakaSeries { Id = 1, Title = "Overlord", Type = "manga" },
                    new MangaBakaSeries { Id = 2, Title = "Overlord: Prologue", Type = "novel" },
                    new MangaBakaSeries { Id = 3, Title = "Spice and Wolf", Type = "Light Novel" },
                    new MangaBakaSeries { Id = 4, Title = "Solo Leveling", Type = "manhwa" },
                    new MangaBakaSeries { Id = 5, Title = "Some Artbook", Type = "other" },
                    new MangaBakaSeries { Id = 6, Title = "Untyped", Type = null },
                },
            };
            SetupSearchMock(searchResource);

            var results = Subject.SearchForNewManga("overlord");

            results.Select(m => m.Title)
                   .Should().BeEquivalentTo("Overlord", "Solo Leveling", "Some Artbook", "Untyped");
        }

        // The canonical `title` for a non-Latin work is often a romanization (e.g.
        // "Ichyeojin Deulpan"); the recognizable English title lives in titles[] as the
        // language=="en", is_primary==true entry. SelectPreferredTitle must surface that so the
        // library/Add picker shows "The Forgotten Field", not the romanization.
        [Test]
        public void GetMangaInfo_prefers_primary_english_title_over_romanized_canonical()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 586650,
                    Title = "Ichyeojin Deulpan",
                    RomanizedTitle = "Ichyeojin Deulpan",
                    NativeTitle = "잊혀진 들판",
                    Titles = new List<MangaBakaTitleEntry>
                    {
                        new MangaBakaTitleEntry { Language = "ko", Title = "잊혀진 들판", IsPrimary = true },
                        new MangaBakaTitleEntry { Language = "en", Title = "Ichojin Deulpan", IsPrimary = false },
                        new MangaBakaTitleEntry { Language = "ko-Latn", Title = "Ichyeojin Deulpan", IsPrimary = true },
                        new MangaBakaTitleEntry { Language = "en", Title = "The Forgotten Field", IsPrimary = true },
                    },
                },
            };
            SetupGetByIdMock(resource);

            Subject.GetMangaInfo("586650").Item1.Title.Should().Be("The Forgotten Field");
        }

        // No primary English title in titles[] → fall back to the canonical `title` (never a
        // non-primary English alias, which could be a worse romanization).
        [Test]
        public void GetMangaInfo_falls_back_to_canonical_title_when_no_primary_english()
        {
            var resource = new MangaBakaSeriesResource
            {
                Data = new MangaBakaSeries
                {
                    Id = 1,
                    Title = "Berserk",
                    Titles = new List<MangaBakaTitleEntry>
                    {
                        new MangaBakaTitleEntry { Language = "ja", Title = "ベルセルク", IsPrimary = true },
                        new MangaBakaTitleEntry { Language = "en", Title = "Berserk (alt)", IsPrimary = false },
                    },
                },
            };
            SetupGetByIdMock(resource);

            Subject.GetMangaInfo("1").Item1.Title.Should().Be("Berserk");
        }

        // D-07: MangaBaka has no AniList/MAL reverse-lookup endpoint — both overloads return
        // empty (documented capability gap, NOT a swallowed failure).
        [Test]
        public void SearchByCrossSourceId_returns_empty_capability_gap()
        {
            Subject.SearchForNewMangaByAniListId(105398).Should().BeEmpty();
            Subject.SearchForNewMangaByMalId(121496).Should().BeEmpty();
        }

        [Test]
        public void GetMangaInfo_throws_MangaNotFoundException_on_404()
        {
            // MangaBakaApi.GetById sets req.SuppressHttpError=true and inspects the response's
            // StatusCode for NotFound; it does NOT swallow a thrown HttpException. The mock
            // therefore RETURNS a 404 HttpResponse rather than throwing.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaBakaSeriesResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var raw = new HttpResponse(req, headers, "{}", HttpStatusCode.NotFound);
                      return new HttpResponse<MangaBakaSeriesResource>(raw);
                  });

            Action act = () => Subject.GetMangaInfo("3397");

            act.Should().Throw<MangaNotFoundException>();
        }

        // D-09: Test() runs a canned ?q= search (no /ping endpoint exists) and returns a valid
        // result on a 200 — without throwing and without surfacing any secret.
        [Test]
        public void Test_returns_ok_for_canned_search()
        {
            SetupSearchMock(new MangaBakaSearchResource { Data = new List<MangaBakaSeries>() });

            Subject.Test().IsValid.Should().BeTrue();
        }

        // ---- Helpers ----

        private void SetupGetByIdMock(MangaBakaSeriesResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaBakaSeriesResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(resource);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaBakaSeriesResource>(raw);
                  });
        }

        private void SetupSearchMock(MangaBakaSearchResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<MangaBakaSearchResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(resource);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<MangaBakaSearchResource>(raw);
                  });
        }
    }
}
