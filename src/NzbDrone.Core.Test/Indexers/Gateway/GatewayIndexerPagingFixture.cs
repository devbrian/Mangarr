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
    /// quick task 260620-ing — proves the SEARCH path now WALKS the bounded lazy offset sequence
    /// through the kept <see cref="HttpIndexerBase{TSettings}.FetchReleases"/> engine (paging ON via
    /// the dynamic <c>PageSize</c> override) and STOPS at the first short page. Also proves the
    /// self-correcting bound + guid-dedup defenses against an offset-ignoring gateway (T-ing-01 /
    /// T-ing-02). SetUp mirrors <see cref="GatewayIndexerFixture"/>; page JSON is generated
    /// programmatically (no new fixture files) and the HTTP stub branches on the requested
    /// <c>Offset</c> so the walk is driven deterministically.
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
        public async Task Fetch_terminates_and_dedupes_under_offset_ignoring_gateway()
        {
            ((GatewaySettings)Subject.Definition.Settings).ResultLimit = 100;

            // Simulate a gateway that ACCEPTS but IGNORES offset: every page is the SAME full
            // 100-row stableGuids page. The walk must still TERMINATE (it never sees a short page) at
            // the generator's max-page cap = ceil(1000/100) = 10, backstopped by the engine's
            // MaxNumResultsPerQuery, and the kept CleanupReleases guid-dedup collapses the repeats.
            StubPages(_ => PageJson(0, 100, stableGuids: true));

            var releases = await Subject.Fetch(Criteria());

            _requestedOffsets.Count.Should().Be(10,
                "ceil(MaxSearchResults=1000 / 100) = 10 — bounded, never an infinite loop");
            _requestedOffsets.Should().Equal(Enumerable.Range(0, 10).Select(p => p * 100),
                "offsets advance 0, 100, …, 900 even though the gateway ignores them");

            releases.Should().HaveCount(100, "guid-dedup collapses the 10 identical pages to 100 unique releases");
            releases.Select(r => r.Guid).Should().OnlyHaveUniqueItems();
        }
    }
}
