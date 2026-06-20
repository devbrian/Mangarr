using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// quick task 260620-ing — proves the SEARCH paging behavior:
    /// <list type="bullet">
    /// <item>AUTOMATIC search WALKS the full-coverage offset sequence through the kept
    /// <see cref="HttpIndexerBase{TSettings}.FetchReleases"/> engine (paging ON via the dynamic
    /// <c>PageSize</c> override + the lifted <c>MaxNumResultsPerQuery</c>) and STOPS at the first
    /// short page.</item>
    /// <item>INTERACTIVE search (Search tab) fetches only the FIRST page (one request).</item>
    /// <item>An offset-REGRESSING gateway is bounded by the generator's <c>MaxSearchPages</c> guard +
    /// guid-dedup — no infinite loop.</item>
    /// </list>
    /// SetUp mirrors <see cref="GatewayIndexerFixture"/>; page JSON is generated programmatically (no
    /// new fixture files) and the HTTP stub branches on the requested <c>Offset</c> so the walk is
    /// driven deterministically.
    /// </summary>
    [TestFixture]
    public class GatewayIndexerPagingFixture : CoreTest<GatewayIndexer>
    {
        private GatewayCapabilities _capabilities;
        private List<int> _requestedOffsets;

        [SetUp]
        public void Setup()
        {
            _requestedOffsets = new List<int>();

            Subject.Definition = new IndexerDefinition
            {
                Id = 99,
                Name = "Mangarr Gateway",
                Settings = new GatewaySettings
                {
                    BaseUrl = "http://localhost:9191",
                    ApiKey = "k"
                }
            };

            // caps.json advertises defaultPageSize=50; the tests drive page size deterministically
            // via a Settings.ResultLimit override (the single-source-of-truth ResolveEffectiveLimit
            // ladder), so the engine's IsFullPage threshold equals the override.
            _capabilities = JsonConvert.DeserializeObject<GatewayCapabilities>(
                File.ReadAllText("Files/Indexers/Gateway/caps.json"));

            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Returns(_capabilities);
        }

        // Emits a ReleaseListResponse with `count` rows (no warnings ⇒ no ExpectedWarns). Each row
        // carries a non-empty guid/title/downloadHandle so IsValidRelease passes; mangaTitle and
        // chapterNumber are null so BuildTitle returns the verbatim (non-empty) title. With
        // stableGuids the guid is `gw:fixed:{i}` (identical across pages → dedup target); otherwise
        // `gw:{offset}:{i}` (unique per page).
        private static string PageJson(int offset, int count, bool stableGuids)
        {
            var rows = Enumerable.Range(0, count).Select(i =>
            {
                var guid = stableGuids ? $"gw:fixed:{i}" : $"gw:{offset}:{i}";
                return new
                {
                    guid,
                    title = $"Release {guid}",
                    sourceKey = "comix.to",
                    downloadHandle = $"R6.{guid}",
                    publishDate = "2026-06-01T12:00:00Z",
                    mangaTitle = (string)null,
                    chapterNumber = (decimal?)null
                };
            });

            return JsonConvert.SerializeObject(new { releases = rows, warnings = Array.Empty<object>() });
        }

        // Stub IHttpClient.ExecuteAsync: read the POST body, recover the requested Offset, record it,
        // and return the page the test maps that offset to.
        private void StubPages(Func<int, string> responseByOffset)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      var json = Encoding.UTF8.GetString(req.ContentData);
                      var body = JsonConvert.DeserializeObject<GatewaySearchRequest>(json);

                      _requestedOffsets.Add(body.Offset);

                      var content = responseByOffset(body.Offset);
                      return Task.FromResult(new HttpResponse(req, new HttpHeader(), content));
                  });
        }

        private static MangaSearchCriteria Criteria()
            => new MangaSearchCriteria { Manga = new NzbDrone.Core.Manga.Manga { Title = "Solo Leveling" } };

        [Test]
        public async Task Fetch_walks_multiple_full_pages_and_stops_on_short_page()
        {
            ((GatewaySettings)Subject.Definition.Settings).ResultLimit = 100;

            // offset 0 → a FULL page (100 unique rows) ⇒ engine advances; offset 100 → a SHORT page
            // (30 rows < limit) ⇒ IsFullPage false ⇒ engine stops. Any further offset is unexpected.
            StubPages(offset => offset switch
            {
                0 => PageJson(0, 100, stableGuids: false),
                100 => PageJson(100, 30, stableGuids: false),
                _ => PageJson(offset, 0, stableGuids: false)
            });

            var releases = await Subject.Fetch(Criteria());

            _requestedOffsets.Should().Equal(new[] { 0, 100 },
                "the engine advanced past the first full page then stopped after the short page");
            _requestedOffsets.Count.Should().Be(2, "exactly two HTTP calls — no page beyond the short one");

            releases.Should().HaveCount(130, "all 100 + 30 unique rows are kept; none dropped");
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public async Task Fetch_interactive_search_fetches_only_first_page()
        {
            ((GatewaySettings)Subject.Definition.Settings).ResultLimit = 100;

            // Even though every offset would return a FULL page (so the engine WOULD keep walking on
            // the automatic path), an INTERACTIVE search emits a single page — the Search tab shows
            // only the first page (quick task 260620-ing).
            StubPages(offset => PageJson(offset, 100, stableGuids: false));

            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga { Title = "Solo Leveling" },
                InteractiveSearch = true
            };

            var releases = await Subject.Fetch(criteria);

            _requestedOffsets.Should().Equal(new[] { 0 }, "interactive search fetches exactly one page (offset 0)");
            releases.Should().HaveCount(100, "the single first page is returned verbatim");
        }

        [Test]
        public async Task Fetch_terminates_under_offset_regressing_gateway()
        {
            // Page size 10 keeps the test light; the bound is independent of it.
            ((GatewaySettings)Subject.Definition.Settings).ResultLimit = 10;

            // Simulate a gateway that ACCEPTS but IGNORES offset (a regression from today's working
            // behavior): every page is the SAME full 10-row stableGuids page, so the engine never sees
            // a short page. The walk must still TERMINATE at the generator's MaxSearchPages runaway
            // guard (1000), and the kept CleanupReleases guid-dedup collapses the repeats — no
            // infinite loop is constructible even with MaxNumResultsPerQuery lifted for full coverage.
            StubPages(_ => PageJson(0, 10, stableGuids: true));

            var releases = await Subject.Fetch(Criteria());

            _requestedOffsets.Count.Should().Be(1000,
                "bounded by MaxSearchPages — never an infinite loop even when offset is ignored");

            // offsets advance by the page size even though the gateway ignores them
            _requestedOffsets.Take(4).Should().Equal(0, 10, 20, 30);

            releases.Should().HaveCount(10, "guid-dedup collapses the identical pages to 10 unique releases");
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }
    }
}
