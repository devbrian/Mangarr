using System.Collections.Generic;
using System.Linq;
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
using NzbDrone.Test.Common;

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

        // Bug fix regression (search-titles-romanized, 2026-05-09): when MangaDex
        // ships `attributes.title` with ONLY a romanized key (`ko-ro` for Korean,
        // `ja-ro` for Japanese) and the official English title lives under
        // `attributes.altTitles[].en`, the search-result mapping MUST prefer the
        // English alt-title over the romanization. Without this, the AddManga UI
        // shows e.g. "Na Honjaman Level Up: Ragnarok" instead of "Solo Leveling:
        // Ragnarok" for MangaDex id ade0306c-f4b6-4890-9edb-1ddf04df2039.
        [Test]
        public void Search_prefers_english_altTitle_over_romanized_canonical_title()
        {
            // Shape mirrors the live MangaDex response for
            // ade0306c-f4b6-4890-9edb-1ddf04df2039 (Solo Leveling: Ragnarok).
            var listResource = new MangaListResource
            {
                Data = new List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "ade0306c-f4b6-4890-9edb-1ddf04df2039",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new Dictionary<string, string>
                            {
                                { "ko-ro", "Na Honjaman Level Up: Ragnarok" },
                            },
                            AltTitles = new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "ko", "나 혼자만 레벨업 : 라그나로크" } },
                                new Dictionary<string, string> { { "pt-br", "Upando Sozinho: Ragnarok" } },
                                new Dictionary<string, string> { { "en", "Solo Leveling: Ragnarok" } },
                                new Dictionary<string, string> { { "vi", "Tôi Thăng Cấp" } },
                            },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("solo leveling");

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Solo Leveling: Ragnarok");
        }

        // When multiple `en` entries exist in altTitles, the FIRST one wins —
        // MangaDex maintainers list the canonical English title first by
        // convention; subsequent `en` entries are alternate spellings or fan
        // translations. Shape mirrors the live response for
        // 32d76d19-8a05-4db0-9fc2-e0b0648fe9d0 (Solo Leveling) — first `en` is
        // "Solo Leveling", second `en` is "I level up alone".
        [Test]
        public void Search_prefers_first_english_altTitle_when_multiple_en_entries_exist()
        {
            var listResource = new MangaListResource
            {
                Data = new List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "32d76d19-8a05-4db0-9fc2-e0b0648fe9d0",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new Dictionary<string, string>
                            {
                                { "ko-ro", "Na Honjaman Level-Up" },
                            },
                            AltTitles = new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "ko", "나 혼자만 레벨업" } },
                                new Dictionary<string, string> { { "en", "Solo Leveling" } },
                                new Dictionary<string, string> { { "ko-ro", "Na Honjaman Lebel-eob" } },
                                new Dictionary<string, string> { { "en", "I level up alone" } },
                            },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("solo leveling");

            results[0].Title.Should().Be("Solo Leveling");
        }

        // Canonical title["en"] still wins outright when present — the previous
        // happy path (English-canonical-title) must not regress. Many existing
        // English-original manga (e.g. "Naruto", "Bleach") rely on this path.
        [Test]
        public void Search_prefers_canonical_english_title_over_altTitles()
        {
            var listResource = new MangaListResource
            {
                Data = new List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "cccccccc-cccc-cccc-cccc-cccccccccccc",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new Dictionary<string, string>
                            {
                                { "en", "One Piece" },
                                { "ja", "ワンピース" },
                            },
                            AltTitles = new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "en", "Wan Piisu (Wrong)" } },
                            },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("one piece");

            results[0].Title.Should().Be("One Piece");
        }

        // Final-fallback path: when neither title["en"] NOR any altTitles[].en
        // exists, fall through to the first non-empty value of `title` (i.e.
        // the romanized form). Better than null. Mirrors the pre-fix behavior
        // for records that genuinely have no English entry anywhere.
        [Test]
        public void Search_falls_back_to_first_title_value_when_no_english_anywhere()
        {
            var listResource = new MangaListResource
            {
                Data = new List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "dddddddd-dddd-dddd-dddd-dddddddddddd",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new Dictionary<string, string>
                            {
                                { "ja-ro", "Aru Manga" },
                            },
                            AltTitles = new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "ja", "ある漫画" } },
                            },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("aru manga");

            results[0].Title.Should().Be("Aru Manga");
        }

        // Edge case: empty `en` value in canonical title with a populated `en`
        // entry in altTitles — the empty canonical must not win over the
        // non-empty alt. Mirrors the WR-06 "empty en" gotcha that the
        // pre-existing PreferredString already guards against, applied to the
        // new altTitles dimension.
        [Test]
        public void Search_skips_empty_canonical_en_and_uses_altTitle_en()
        {
            var listResource = new MangaListResource
            {
                Data = new List<MangaDataItem>
                {
                    new MangaDataItem
                    {
                        Id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                        Type = "manga",
                        Attributes = new MangaAttributes
                        {
                            Title = new Dictionary<string, string>
                            {
                                { "en", "" },
                                { "ko-ro", "Romanized" },
                            },
                            AltTitles = new List<Dictionary<string, string>>
                            {
                                new Dictionary<string, string> { { "en", "English Official" } },
                            },
                        },
                    },
                },
            };
            SetupSearchMock(listResource);

            var results = Subject.SearchForNewManga("anything");

            results[0].Title.Should().Be("English Official");
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

        // BL-07 regression: GetFeed must terminate at the MaxFeedPages cap if the
        // upstream returns a full page (limit=500) forever — a bug in the server, an
        // unbounded result chain, or simply a manga with > MaxFeedPages * pageSize
        // chapters. Without the cap the loop is `while (true)` and never exits.
        [Test]
        public void GetMangaInfo_GetFeed_terminates_at_MaxFeedPages_cap_and_warns()
        {
            // GetById returns minimal valid manga so we get to the GetFeed call.
            var mangaResource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "abcdef01-2345-6789-abcd-ef0123456789",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new Dictionary<string, string> { { "en", "Eternal Manga" } },
                    },
                },
            };
            SetupGetByIdMock(mangaResource);

            // Mock /feed to ALWAYS return a full page (500 entries) — the cap is the
            // only thing that can break the loop. Each entry needs distinct id +
            // chapter so JSON serialization succeeds.
            var fullPage = new ChapterFeedResource
            {
                Data = Enumerable.Range(0, 500).Select(i => new ChapterFeedEntry
                {
                    Id = $"feed-entry-{i:D6}",
                    Type = "chapter",
                    Attributes = new ChapterFeedAttributes
                    {
                        Chapter = i.ToString(),
                        TranslatedLanguage = "en",
                    },
                }).ToList(),
            };

            var feedCallCount = 0;
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<ChapterFeedResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      feedCallCount++;
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(fullPage);
                      var raw = new HttpResponse(req, headers, body, HttpStatusCode.OK);
                      return new HttpResponse<ChapterFeedResource>(raw);
                  });

            var result = Subject.GetMangaInfo("abcdef01-2345-6789-abcd-ef0123456789");

            // Loop must have terminated — exact upper bound is the MaxFeedPages
            // constant inside MangaDexApi (currently 50). We only require that the
            // loop bailed out with a finite count well below "infinity".
            feedCallCount.Should().BeGreaterThan(0);
            feedCallCount.Should().BeLessThanOrEqualTo(100,
                "the feed loop must terminate at the MaxFeedPages cap, not run forever");
            result.Item2.Should().NotBeEmpty();

            // The cap-hit Warn must have been emitted so operators can investigate.
            ExceptionVerification.ExpectedWarns(1);
        }

        // Bug fix regression (manga-details-chapter-titles, 2026-05-10): MangaDex
        // ships chapter feed entries per (chapter, language, scanlation-group). For
        // works with no English translation, non-EN entries (e.g. Polish) routinely
        // carry scanlator-group commentary in the Title slot — entries with body like
        // "KONIEC DRUGIEGO SEZONU !!!!!" or "CHO SOMINO AL-KACHUD WYKONUJE TAKTYCZNY
        // ODWRÓT" pollute the canonical Chapter.Title column on the Manga Details
        // Chapters tab. The Sonarr-mirror canonical Chapter.Title shape (Episode.Title
        // = TVDB English) means non-EN entries' Title slot must be suppressed to null;
        // the chapter ROW itself still ingests structural fields (chapterNumber /
        // volumeNumber / firstReleaseDate / externalId — all language-neutral) from
        // any-language entry.
        [Test]
        public void GetMangaInfo_suppresses_non_english_chapter_titles_to_null()
        {
            var mangaResource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "ade0306c-f4b6-4890-9edb-1ddf04df2039",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new Dictionary<string, string> { { "en", "Solo Leveling: Ragnarok" } },
                    },
                },
            };
            SetupGetByIdMock(mangaResource);

            // Shape mirrors the live MangaDex feed for Solo Leveling: Ragnarok —
            // chapter 3 has Polish-only entries with scanlator commentary in Title
            // and an Italian entry with no title.
            var feed = new ChapterFeedResource
            {
                Data = new List<ChapterFeedEntry>
                {
                    new ChapterFeedEntry
                    {
                        Id = "feed-3-pl-with-noisy-title",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "3",
                            TranslatedLanguage = "pl",
                            Title = "PRZYBYWA BOHATERSKA MRÓWKA STEFAN",
                        },
                    },
                    new ChapterFeedEntry
                    {
                        Id = "feed-3-it-no-title",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "3",
                            TranslatedLanguage = "it",
                            Title = null,
                        },
                    },
                },
            };
            SetupGetFeedMockWith(feed);

            var result = Subject.GetMangaInfo("ade0306c-f4b6-4890-9edb-1ddf04df2039");
            var chapter3 = result.Item2.Single(c => c.ChapterNumber == 3m);

            // The Polish scanlator commentary MUST NOT win the Title slot. With no
            // English entry, the canonical Title stays null and the UI falls back
            // to the "Chapter 3" placeholder via ChapterTitleLink.
            chapter3.Title.Should().BeNull();
        }

        // Bug fix regression (manga-details-chapter-titles, 2026-05-10): when an EN
        // translation is available for a chapter, its Title wins regardless of feed
        // ordering — including when the EN entry appears BEFORE noisy non-EN entries
        // in the feed response. Stable OrderBy(Title == null) inside the GroupBy
        // guarantees the English row wins the Last() race even if it precedes the
        // noisy non-EN rows in feed order.
        [Test]
        public void GetMangaInfo_prefers_english_chapter_title_regardless_of_feed_order()
        {
            var mangaResource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "11111111-1111-1111-1111-111111111111",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new Dictionary<string, string> { { "en", "Test Manga" } },
                    },
                },
            };
            SetupGetByIdMock(mangaResource);

            var feed = new ChapterFeedResource
            {
                Data = new List<ChapterFeedEntry>
                {
                    // EN entry comes FIRST — without the OrderBy(Title == null) fix,
                    // the subsequent pl entry's null title (post-suppression) would
                    // overwrite this via Last().
                    new ChapterFeedEntry
                    {
                        Id = "feed-en-first",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "10",
                            TranslatedLanguage = "en",
                            Title = "The Real English Title",
                        },
                    },
                    new ChapterFeedEntry
                    {
                        Id = "feed-pl-second",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "10",
                            TranslatedLanguage = "pl",
                            Title = "POLISH SCANLATOR COMMENTARY",
                        },
                    },
                    new ChapterFeedEntry
                    {
                        Id = "feed-it-third",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "10",
                            TranslatedLanguage = "it",
                            Title = null,
                        },
                    },
                },
            };
            SetupGetFeedMockWith(feed);

            var result = Subject.GetMangaInfo("11111111-1111-1111-1111-111111111111");
            var chapter10 = result.Item2.Single(c => c.ChapterNumber == 10m);

            chapter10.Title.Should().Be("The Real English Title");
        }

        // Bug fix regression (manga-details-chapter-titles, 2026-05-10): structural
        // fields (ChapterNumber / VolumeNumber / FirstReleaseDate / ExternalId) still
        // ingest from any-language entry. Chapters that exist ONLY in non-English
        // translations must remain enumerable + searchable — they just render as
        // "Chapter N" because the Title slot is suppressed. This guards against the
        // over-aggressive fix of dropping entire rows for non-EN chapters.
        [Test]
        public void GetMangaInfo_ingests_structural_fields_from_non_english_only_entries()
        {
            var mangaResource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "22222222-2222-2222-2222-222222222222",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new Dictionary<string, string> { { "en", "Test Manga 2" } },
                    },
                },
            };
            SetupGetByIdMock(mangaResource);

            var publishDate = new System.DateTime(2024, 6, 15, 12, 0, 0, System.DateTimeKind.Utc);
            var feed = new ChapterFeedResource
            {
                Data = new List<ChapterFeedEntry>
                {
                    new ChapterFeedEntry
                    {
                        Id = "feed-42-pl",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "42",
                            Volume = "3",
                            TranslatedLanguage = "pl",
                            Title = "noisy pl commentary",
                            PublishAt = publishDate,
                        },
                    },
                },
            };
            SetupGetFeedMockWith(feed);

            var result = Subject.GetMangaInfo("22222222-2222-2222-2222-222222222222");
            var chapter = result.Item2.Single();

            chapter.ChapterNumber.Should().Be(42m);
            chapter.VolumeNumber.Should().Be(3);
            chapter.FirstReleaseDate.Should().Be(publishDate);
            chapter.ExternalId.Should().Be("feed-42-pl");
            chapter.Title.Should().BeNull("non-English Title must be suppressed");
        }

        // Bug fix regression (manga-details-chapter-titles, 2026-05-10): case-insensitive
        // language code match for the "en" canonical check. MangaDex's BCP-47 codes are
        // lowercase per the official API contract, but a defensive equality (OrdinalIgnoreCase)
        // is preferred to avoid silent regressions if upstream ever ships "EN" / "En".
        [Test]
        public void GetMangaInfo_treats_english_language_code_case_insensitively()
        {
            var mangaResource = new MangaResource
            {
                Data = new MangaDataItem
                {
                    Id = "44444444-4444-4444-4444-444444444444",
                    Type = "manga",
                    Attributes = new MangaAttributes
                    {
                        Title = new Dictionary<string, string> { { "en", "Test Manga 4" } },
                    },
                },
            };
            SetupGetByIdMock(mangaResource);

            var feed = new ChapterFeedResource
            {
                Data = new List<ChapterFeedEntry>
                {
                    new ChapterFeedEntry
                    {
                        Id = "feed-mixed-case-en",
                        Type = "chapter",
                        Attributes = new ChapterFeedAttributes
                        {
                            Chapter = "1",
                            TranslatedLanguage = "EN",  // uppercase
                            Title = "Mixed-Case English Title",
                        },
                    },
                },
            };
            SetupGetFeedMockWith(feed);

            var result = Subject.GetMangaInfo("44444444-4444-4444-4444-444444444444");
            var chapter1 = result.Item2.Single();

            chapter1.Title.Should().Be("Mixed-Case English Title");
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

        // Bug fix (manga-details-chapter-titles, 2026-05-10): helper for the
        // Title-suppression test trio — returns a populated feed for the single
        // /manga/{id}/feed call. The MangaDexApi.GetFeed loop exits when a page
        // returns < pageSize entries, so a single-page mock works as long as the
        // payload's Data count is < 500.
        private void SetupGetFeedMockWith(ChapterFeedResource resource)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get<ChapterFeedResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var body = JsonConvert.SerializeObject(resource);
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
