using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="MangaDexRequestGenerator"/>. References NOT-YET-BUILT
    /// production type (lands in Plan 03-04).
    ///
    /// Coverage:
    /// - SOURCE-01: URL composition (BaseUrl + endpoint + query params)
    /// - SOURCE-04: paging — limit=500 per RESEARCH §Per-Port API Discovery
    /// - SOURCE-07: D-03 — TV criteria overloads (SeasonSearchCriteria etc.) return empty chain
    /// </summary>
    [TestFixture]
    public class MangaDexRequestGeneratorFixture : CoreTest<MangaDexRequestGenerator>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Settings = new MangaDexIndexerSettings
            {
                BaseUrl = "https://api.mangadex.org",
                SourceKey = "mangadex"
            };
        }

        [Test]
        public void GetRecentRequests_hits_chapter_endpoint_with_publishAt_desc()
        {
            var chain = Subject.GetRecentRequests();
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/chapter");
            url.Should().Contain("order[publishAt]=desc");
            url.Should().Contain("includes[]=scanlation_group");
        }

        [Test]
        public void GetSearchRequests_MangaSearchCriteria_targets_manga_id_feed()
        {
            var manga = new Manga.Manga
            {
                MangaDexId = Guid.Parse("a1c7c817-4e59-43b7-9365-09675a149a6f")
            };
            var criteria = new MangaSearchCriteria { Manga = manga };
            var chain = Subject.GetSearchRequests(criteria);
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/manga/a1c7c817-4e59-43b7-9365-09675a149a6f/feed");
            url.Should().Contain("limit=500");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_uses_chapter_point_query()
        {
            var manga = new Manga.Manga
            {
                MangaDexId = Guid.Parse("a1c7c817-4e59-43b7-9365-09675a149a6f")
            };

            // Phase 16 STRUCT-01 + Phase 16.1: canonical Chapter is language-free; per-translation
            // language flows via ParsedChapterInfo (parser grain) / ChapterFile (file grain).
            var chapter = new Manga.Chapter
            {
                ChapterNumber = 42m,
            };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };
            var chain = Subject.GetSearchRequests(criteria);
            var url = chain.GetAllTiers().First().First().Url.FullUri;

            url.Should().Contain("/chapter?");
            url.Should().Contain("manga=a1c7c817-4e59-43b7-9365-09675a149a6f");
            url.Should().Contain("chapter[]=42");
            url.Should().NotContain("/feed");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_with_decimal_chapter_round_trips()
        {
            var manga = new Manga.Manga
            {
                MangaDexId = Guid.Parse("a1c7c817-4e59-43b7-9365-09675a149a6f")
            };
            var chapter = new Manga.Chapter { ChapterNumber = 12.5m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };
            var chain = Subject.GetSearchRequests(criteria);
            var url = chain.GetAllTiers().First().First().Url.FullUri;

            url.Should().Contain("chapter[]=12.5");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_returns_empty_when_no_MangaDexId()
        {
            var manga = new Manga.Manga { MangaDexId = null };
            var chapter = new Manga.Chapter { ChapterNumber = 1m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };
            var chain = Subject.GetSearchRequests(criteria);
            chain.GetAllTiers().Should().BeEmpty();
        }

        // Regression — berserk-no-chapters debug session (2026-05-13):
        // The MangaDex /feed and /chapter endpoints filter by the PARENT manga's
        // contentRating when contentRating[] is set. A hardcoded `safe + suggestive`
        // allow-list silently dropped every chapter for any erotica/pornographic
        // manga the user added (Berserk is rated `erotica`). The filter has been
        // removed across all 3 URL builders + their fallback URLs so MangaDex
        // returns all ratings; if editorial-policy gating is re-introduced it must
        // be a user-visible setting, not a hardcoded default. These three tests
        // pin the absence so the regression cannot reappear silently.
        [Test]
        public void GetRecentRequests_must_not_filter_by_contentRating()
        {
            var url = Subject.GetRecentRequests().GetAllTiers().First().First().Url.FullUri;
            url.Should().NotContain("contentRating",
                "MangaDex feed must return all parent-manga ratings; the hardcoded safe+suggestive filter silently dropped chapters for erotica/pornographic-rated manga the user already added (berserk-no-chapters, 2026-05-13)");
        }

        [Test]
        public void GetSearchRequests_MangaSearchCriteria_must_not_filter_by_contentRating()
        {
            var manga = new Manga.Manga
            {
                MangaDexId = Guid.Parse("a1c7c817-4e59-43b7-9365-09675a149a6f")
            };
            var criteria = new MangaSearchCriteria { Manga = manga };
            var url = Subject.GetSearchRequests(criteria).GetAllTiers().First().First().Url.FullUri;
            url.Should().NotContain("contentRating",
                "see GetRecentRequests_must_not_filter_by_contentRating — same root cause, same fix");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_must_not_filter_by_contentRating()
        {
            var manga = new Manga.Manga
            {
                MangaDexId = Guid.Parse("a1c7c817-4e59-43b7-9365-09675a149a6f")
            };
            var chapter = new Manga.Chapter { ChapterNumber = 1m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };
            var url = Subject.GetSearchRequests(criteria).GetAllTiers().First().First().Url.FullUri;
            url.Should().NotContain("contentRating",
                "see GetRecentRequests_must_not_filter_by_contentRating — same root cause, same fix");
        }
    }
}
