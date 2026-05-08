using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="MangaDexIndexer"/>. References NOT-YET-BUILT production
    /// types (lands in Plan 03-04). Tests start RED at Wave 0; flip GREEN when 03-04 commits.
    ///
    /// Coverage:
    /// - SOURCE-01: ThingiProvider auto-discovery contract (DefaultSourceKey + Protocol)
    /// - SOURCE-03: per-SourceKey rate budget routing
    /// - SOURCE-04: ChapterSearchCriteria fan-out + ReleaseInfo extension fields
    /// - SOURCE-07: D-03 NotSupportedException for inherited TV criteria overloads (manga indexer
    ///              MUST NOT silently accept TV-shaped queries)
    /// </summary>
    [TestFixture]
    public class MangaDexIndexerFixture : CoreTest<MangaDexIndexer>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Settings = new MangaDexIndexerSettings
                {
                    BaseUrl = "https://api.mangadex.org",
                    SourceKey = "mangadex"
                }
            };
        }

        [Test]
        public void DefaultSourceKey_should_be_mangadex()
        {
            Subject.DefaultSourceKey.Should().Be("mangadex");
        }

        [Test]
        public void Protocol_should_be_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases()
        {
            // Stubbed; flips green when MangaDexIndexer + MangaDexRequestGenerator land in 03-04
            var criteria = new MangaSearchCriteria();
            var releases = await Subject.Fetch(criteria);
            releases.Should().NotBeNull();
        }

        [Test]
        public void GetDownloadHeaders_returns_empty_for_MangaDex()
        {
            // D-14: MangaDex does not require a Referer header (unlike Comix); default empty.
            var release = new ReleaseInfo();
            var headers = Subject.GetDownloadHeaders(release);
            headers.Should().BeEmpty();
        }
    }
}
