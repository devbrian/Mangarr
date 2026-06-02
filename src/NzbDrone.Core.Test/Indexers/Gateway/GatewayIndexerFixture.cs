using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayIndexer"/> (lands in Plan 04 Task 2). Mirrors the
    /// <c>ComixIndexerFixture</c> <c>CoreTest&lt;GatewayIndexer&gt;</c> + <c>Subject.Definition</c>
    /// SetUp shape. Covers GWIX-02 (the <c>gatewaySources</c> RequestAction forces a live caps
    /// refetch), the Protocol/Name contract, the Fetch → <c>FetchReleases</c> wiring, and GWIX-03
    /// (Guid-dedup + identity-stamping by the kept <c>CleanupReleases</c> engine). Also asserts the
    /// A3 disabled-by-default seed (empty default settings fail validation → EnableInteractiveSearch
    /// == false).
    /// </summary>
    [TestFixture]
    public class GatewayIndexerFixture : CoreTest<GatewayIndexer>
    {
        private GatewayCapabilities _capabilities;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Id = 99,
                Name = "Manga Gateway",
                Settings = new GatewaySettings
                {
                    BaseUrl = "http://localhost:9191",
                    ApiKey = "k"
                }
            };

            _capabilities = JsonConvert.DeserializeObject<GatewayCapabilities>(
                File.ReadAllText("Files/Indexers/Gateway/caps.json"));

            // Background reads use the cached caps; Test() + the dropdown force a live refetch.
            // Both forceRefresh true AND false return the same mixed caps for these tests.
            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Returns(_capabilities);
        }

        [Test]
        public void Protocol_should_be_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public void Name_should_be_Manga_Gateway()
        {
            Subject.Name.Should().Be("Manga Gateway");
        }

        [Test]
        public void gatewaySources_action_forces_caps_refetch()
        {
            // GWIX-02 / D-01: the source dropdown action MUST bypass the 12h cache (forceRefresh:true)
            // so the user always sees the gateway's live source list.
            var result = Subject.RequestAction("gatewaySources", new Dictionary<string, string>());

            result.Should().NotBeNull();

            // The action returns an anonymous { options = [...] } payload; reflect over it.
            var optionsProp = result.GetType().GetProperty("options");
            optionsProp.Should().NotBeNull();

            var options = ((System.Collections.IEnumerable)optionsProp.GetValue(result)).Cast<object>().ToList();
            options.Should().NotBeEmpty();

            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Verify(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), true), Times.Once());
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases()
        {
            StubHttpClient(File.ReadAllText("Files/Indexers/Gateway/search.json"));

            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga { Title = "Solo Leveling" }
            };

            var releases = await Subject.Fetch(criteria);
            releases.Should().NotBeNull();
        }

        [Test]
        public async Task FetchRecent_calls_FetchReleases()
        {
            StubHttpClient(File.ReadAllText("Files/Indexers/Gateway/recent.json"));

            var releases = await Subject.FetchRecent();
            releases.Should().NotBeNull();
        }

        [Test]
        public async Task Fetch_dedupes_by_guid_and_stamps_indexer_identity()
        {
            // GWIX-03: search.json carries a duplicate-guid row (comix.to:solo-leveling:179 appears
            // twice). The kept FetchReleases → CleanupReleases engine MUST collapse it to a single
            // release AND stamp the originating indexer identity onto every release.
            StubHttpClient(File.ReadAllText("Files/Indexers/Gateway/search.json"));

            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga { Title = "Solo Leveling" }
            };

            var releases = await Subject.Fetch(criteria);

            // The duplicate guid collapses (proving CleanupReleases runs via the kept engine).
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
            releases.Count(r => r.Guid == "comix.to:solo-leveling:179").Should().Be(1);

            // Identity-stamped for free by CleanupReleases.
            releases.Should().OnlyContain(r => r.Indexer == "Manga Gateway");
            releases.Should().OnlyContain(r => r.IndexerId == 99);
        }

        [Test]
        public void DefaultDefinitions_seeds_disabled_when_settings_empty()
        {
            // A3 disabled-by-default proof: empty default settings (no BaseUrl/ApiKey) fail
            // config.Validate().IsValid, so DefaultDefinitions seeds EnableInteractiveSearch == false.
            var definition = Subject.DefaultDefinitions
                .OfType<IndexerDefinition>()
                .Single();

            definition.EnableInteractiveSearch.Should().BeFalse();
            definition.EnableRss.Should().BeFalse();
            definition.EnableAutomaticSearch.Should().BeFalse();
        }

        private void StubHttpClient(string content)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                      Task.FromResult(new HttpResponse(req, new HttpHeader(), content)));
        }
    }
}
