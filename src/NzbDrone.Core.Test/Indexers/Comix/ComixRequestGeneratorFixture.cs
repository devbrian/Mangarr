using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="ComixRequestGenerator"/>. References NOT-YET-BUILT
    /// production type (lands in Plan 03-05).
    ///
    /// Coverage:
    /// - URL composition for /api/v2/manga (FetchRecent / latest_updates ordering)
    /// - URL composition for /api/v2/manga/{hash}/chapters (per-manga chapter list)
    /// - D-03: TV criteria overloads return empty chain
    /// </summary>
    [TestFixture]
    public class ComixRequestGeneratorFixture : CoreTest<ComixRequestGenerator>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Settings = new ComixIndexerSettings
            {
                BaseUrl = "https://comix.to",
                SourceKey = "comix.to"
            };
        }

        [Test]
        public void GetRecentRequests_targets_manga_listing_with_chapter_updated_at_desc()
        {
            var chain = Subject.GetRecentRequests();
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/api/v2/manga");
            url.Should().Contain("order[chapter_updated_at]=desc");
        }

        [Test]
        public void GetSearchRequests_MangaSearchCriteria_targets_manga_hash_chapters()
        {
            // Comix uses an opaque per-manga hash_id (not the int manga_id) — Phase 3 will need
            // a comix-specific external-id lookup before /chapters. For Wave 0 this assertion
            // pins the URL contract; the lookup machinery lands with 03-05.
            var manga = new Manga.Manga { Title = "One Piece" };
            var criteria = new MangaSearchCriteria { Manga = manga };
            var chain = Subject.GetSearchRequests(criteria);
            chain.GetAllTiers().Should().NotBeEmpty();
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/api/v2/manga/");
            url.Should().Contain("/chapters");
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
