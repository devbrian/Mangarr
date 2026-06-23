using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Discovery;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaBaka.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Discovery
{
    // Phase 42 Plan 42-02 (DISC-06/02/05/10/03) — DiscoveryService eligibility auto-paging loop +
    // adult-default injection + tag-id binding + genres/tags cache.
    //
    // The MangaBaka provider seam (Browse/GetGenres/GetTags) is mocked — MangaBakaApi is non-DI and
    // cannot be reached for a mock, so the public provider pass-throughs (made virtual in 42-02) are
    // the mock point. DiscoveryService resolves the CONFIGURED provider via IMetadataSourceFactory
    // (All() -> the MangaBaka definition -> GetInstance() -> the configured instance) — NOT the raw
    // DI template (whose null Definition NREs on Settings/Api). The factory mock returns the mocked
    // provider for that resolution. A REAL CacheManager backs the cache so the 2nd GetTags()
    // genuinely hits the in-memory cache.
    [TestFixture]
    public class DiscoveryServiceFixture : CoreTest
    {
        private Mock<MangaBakaMetadataSource> _mangaBaka;
        private CacheManager _cacheManager;
        private DiscoveryService _subject;

        [SetUp]
        public void Setup()
        {
            _mangaBaka = new Mock<MangaBakaMetadataSource>(new Mock<IHttpClient>().Object, TestLogger);
            _cacheManager = new CacheManager();

            // The factory resolves the CONFIGURED MangaBaka instance: All() surfaces its definition,
            // GetInstance() returns the (mocked) provider. This exercises DiscoveryService's real
            // resolution path instead of the template-from-DI-enumerable that NRE'd in production.
            var mangaBakaDefinition = new MetadataSourceDefinition
            {
                Implementation = nameof(MangaBakaMetadataSource)
            };

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.All())
                  .Returns(new List<MetadataSourceDefinition> { mangaBakaDefinition });

            Mocker.GetMock<IMetadataSourceFactory>()
                  .Setup(f => f.GetInstance(It.IsAny<MetadataSourceDefinition>()))
                  .Returns(_mangaBaka.Object);

            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(new List<Manga.Manga>());

            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.All())
                  .Returns(new List<ImportListExclusion>());

            _subject = new DiscoveryService(
                Mocker.GetMock<IMetadataSourceFactory>().Object,
                Mocker.GetMock<IMangaService>().Object,
                Mocker.GetMock<IImportListExclusionService>().Object,
                _cacheManager,
                TestLogger);
        }

        // --- helpers ---

        private static List<MangaBakaSeries> Rows(int start, int count, string state = "active")
        {
            return Enumerable.Range(start, count)
                .Select(i => new MangaBakaSeries { Id = i, Title = "T" + i, State = state })
                .ToList();
        }

        private static MangaBakaSearchResource Page(List<MangaBakaSeries> rows, int total)
        {
            return new MangaBakaSearchResource
            {
                Data = rows,
                Pagination = new MangaBakaPagination { Total = total }
            };
        }

        private void SetupInLibrary(params int[] mangaBakaIds)
        {
            Mocker.GetMock<IMangaService>()
                  .Setup(s => s.GetAllManga())
                  .Returns(mangaBakaIds.Select(id => new Manga.Manga { MangaBakaId = id }).ToList());
        }

        private void SetupExcluded(params int[] mangaBakaIds)
        {
            Mocker.GetMock<IImportListExclusionService>()
                  .Setup(s => s.All())
                  .Returns(mangaBakaIds.Select(id => new ImportListExclusion { MangaBakaId = id }).ToList());
        }

        // --- DISC-06: eligibility loop boundary cases ---

        [Test]
        public void exactly_x_returns_x_and_pool_not_exhausted()
        {
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Returns(Page(Rows(1, 100), 100));

            var result = _subject.Search(new DiscoveryFilter(), 20);

            result.Results.Count.Should().Be(20);
            result.PoolExhausted.Should().BeFalse();
            result.Requested.Should().Be(20);
        }

        [Test]
        public void fewer_than_x_sets_pool_exhausted_with_requested_and_found()
        {
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Returns(Page(Rows(1, 10), 10));

            var result = _subject.Search(new DiscoveryFilter(), 20);

            result.Results.Count.Should().Be(10);
            result.Found.Should().Be(10);
            result.Requested.Should().Be(20);
            result.PoolExhausted.Should().BeTrue();
        }

        [Test]
        public void all_filtered_first_page_pages_forward_until_pool_exhausted()
        {
            // Page 1 entirely in-library (0 eligible) — the loop must NOT stop just because page 1
            // yielded nothing; it pages forward to page 2 where the eligible rows live.
            SetupInLibrary(Enumerable.Range(1, 100).ToArray());

            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), 1, 100))
                      .Returns(Page(Rows(1, 100), 110));
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), 2, 100))
                      .Returns(Page(Rows(101, 10), 110));

            var result = _subject.Search(new DiscoveryFilter(), 5);

            result.Results.Should().OnlyContain(r => r.MangaBakaId >= 101);
            result.Results.Count.Should().Be(5);
            _mangaBaka.Verify(m => m.Browse(It.IsAny<DiscoveryFilter>(), 2, 100), Times.Once());
        }

        [Test]
        public void merged_or_deleted_rows_are_skipped()
        {
            var rows = new List<MangaBakaSeries>
            {
                new MangaBakaSeries { Id = 1, Title = "T1", State = "active" },
                new MangaBakaSeries { Id = 2, Title = "T2", State = "merged" },
                new MangaBakaSeries { Id = 3, Title = "T3", State = "deleted" },
                new MangaBakaSeries { Id = 4, Title = "T4", State = "active" }
            };

            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Returns(Page(rows, 4));

            var result = _subject.Search(new DiscoveryFilter(), 10);

            result.Results.Select(r => r.MangaBakaId).Should().BeEquivalentTo(new[] { 1, 4 });
        }

        [Test]
        public void excluded_rows_are_skipped()
        {
            SetupExcluded(2, 3);

            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Returns(Page(Rows(1, 4), 4));

            var result = _subject.Search(new DiscoveryFilter(), 10);

            result.Results.Select(r => r.MangaBakaId).Should().BeEquivalentTo(new[] { 1, 4 });
        }

        [Test]
        public void ceiling_hit_terminates_at_max_page_with_pool_exhausted()
        {
            // A never-exhausting Browse: every page is full, total is effectively infinite, and
            // every row is in-library so eligible never reaches X. The MAX_PAGE=100 ceiling MUST
            // terminate the loop (no infinite loop) and surface poolExhausted.
            SetupInLibrary(Enumerable.Range(1, 100).ToArray());

            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Returns(() => Page(Rows(1, 100), int.MaxValue));

            var result = _subject.Search(new DiscoveryFilter(), 10);

            result.PoolExhausted.Should().BeTrue();
            result.Results.Should().BeEmpty();
            _mangaBaka.Verify(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()),
                              Times.Exactly(100));
        }

        // --- DISC-05: adult-default injection ---

        [Test]
        public void include_adult_false_injects_safe_and_suggestive_content_rating()
        {
            DiscoveryFilter captured = null;
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Callback<DiscoveryFilter, int, int>((f, p, l) => captured = f)
                      .Returns(Page(Rows(1, 1), 1));

            _subject.Search(new DiscoveryFilter { IncludeAdult = false }, 10);

            captured.ContentRating.Should().BeEquivalentTo(new[] { "safe", "suggestive" });
        }

        [Test]
        public void include_adult_true_does_not_inject_content_rating()
        {
            DiscoveryFilter captured = null;
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Callback<DiscoveryFilter, int, int>((f, p, l) => captured = f)
                      .Returns(Page(Rows(1, 1), 1));

            _subject.Search(new DiscoveryFilter { IncludeAdult = true }, 10);

            captured.ContentRating.Should().BeEmpty();
        }

        // --- DISC-10: tag ids forwarded as integers ---

        [Test]
        public void tag_and_tag_not_are_forwarded_as_integer_ids()
        {
            DiscoveryFilter captured = null;
            _mangaBaka.Setup(m => m.Browse(It.IsAny<DiscoveryFilter>(), It.IsAny<int>(), It.IsAny<int>()))
                      .Callback<DiscoveryFilter, int, int>((f, p, l) => captured = f)
                      .Returns(Page(Rows(1, 1), 1));

            _subject.Search(
                new DiscoveryFilter { Tag = new List<int> { 5, 9 }, TagNot = new List<int> { 3 } },
                10);

            // List<int> by type — these are integer ids end-to-end (D-10), never tag names.
            captured.Tag.Should().BeEquivalentTo(new[] { 5, 9 });
            captured.TagNot.Should().BeEquivalentTo(new[] { 3 });
        }

        // --- DISC-03: genres/tags cached + slim ---

        [Test]
        public void get_tags_caches_so_second_call_does_not_hit_provider()
        {
            _mangaBaka.Setup(m => m.GetTags())
                      .Returns(new List<MangaBakaTag> { new MangaBakaTag { Id = 1, Name = "Adventure" } });

            var first = _subject.GetTags();
            var second = _subject.GetTags();

            first.Should().BeSameAs(second);
            _mangaBaka.Verify(m => m.GetTags(), Times.Once());
        }

        [Test]
        public void get_genres_caches_so_second_call_does_not_hit_provider()
        {
            _mangaBaka.Setup(m => m.GetGenres())
                      .Returns(new List<MangaBakaGenre> { new MangaBakaGenre { Label = "Action", Value = "action" } });

            _subject.GetGenres();
            _subject.GetGenres();

            _mangaBaka.Verify(m => m.GetGenres(), Times.Once());
        }

        [Test]
        public void tag_option_dto_is_slim_with_no_description_member()
        {
            // DISC-03: the tag option payload deliberately omits the long blurb — no Description
            // member exists to populate.
            typeof(MangaBakaTag).GetProperty("Description").Should().BeNull();
        }
    }
}
