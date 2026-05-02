using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
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
        public void GetSearchRequests_SeasonSearchCriteria_returns_empty_chain()
        {
            // D-03: TV criteria → no search requests emitted (no-op fan-out)
            var chain = Subject.GetSearchRequests(new SeasonSearchCriteria());
            chain.GetAllTiers().Should().BeEmpty();
        }
    }
}
