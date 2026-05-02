using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="ComixIndexer"/>. References NOT-YET-BUILT production
    /// types (lands in Plan 03-05). Mirrors <c>MangaDexIndexerFixture</c> shape; differences:
    ///
    /// - <c>SourceKey == "comix.to"</c> (D-12 verbatim site-name convention)
    /// - <see cref="ComixIndexer.GetDownloadHeaders"/> returns <c>Referer: https://comix.to/</c>
    ///   (D-14 — required by upstream comix.to API per keiyoushi extensions PR #11658)
    /// - NO UA-hidden reflection test (comix.to allows UA override per RESEARCH §anti-bot)
    /// </summary>
    [TestFixture]
    public class ComixIndexerFixture : CoreTest<ComixIndexer>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Id = 2,
                Name = "Comix",
                Settings = new ComixIndexerSettings
                {
                    BaseUrl = "https://comix.to",
                    SourceKey = "comix.to"
                }
            };
        }

        [Test]
        public void DefaultSourceKey_should_be_comix_to()
        {
            Subject.DefaultSourceKey.Should().Be("comix.to");
        }

        [Test]
        public void Protocol_should_be_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases()
        {
            var criteria = new MangaSearchCriteria();
            var releases = await Subject.Fetch(criteria);
            releases.Should().NotBeNull();
        }

        [Test]
        public void Fetch_SeasonSearchCriteria_throws_NotSupportedException()
        {
            var criteria = new SeasonSearchCriteria();
            FluentActions.Awaiting(() => Subject.Fetch(criteria))
                .Should().ThrowAsync<System.NotSupportedException>()
                .WithMessage("*manga indexer*");
        }

        [Test]
        public void GetDownloadHeaders_returns_Referer_for_comix()
        {
            // D-14: comix.to requires Referer header on chapter requests.
            var release = new ReleaseInfo();
            var headers = Subject.GetDownloadHeaders(release);
            headers.Should().ContainKey("Referer");
            headers["Referer"].Should().Be("https://comix.to/");
        }
    }
}
