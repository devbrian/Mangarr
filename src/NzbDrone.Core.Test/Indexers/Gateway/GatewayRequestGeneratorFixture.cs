using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayRequestGenerator"/> (lands in Plan 37-02 Task 2 — RED
    /// until then). Loads the Plan-01 <c>caps.json</c> (mixed enabled/searchable/disabled sources)
    /// into <c>Subject.Capabilities</c> and a known <c>GatewaySettings</c> into <c>Subject.Settings</c>.
    ///
    /// Coverage:
    /// - GWIX-04 / D-03: <c>disabled_source_skipped_no_recordfailure</c> — the <c>enabled:false</c>
    ///   caps source is ABSENT from the serialized <c>sources[]</c>; the generator wires NO
    ///   <c>IIndexerSourceStatusService</c> (skip-only is structural, request-side).
    /// - GWIX-03: POST /search with X-Api-Key + always-present query; interactive mapping; chapter
    ///   param; conditional-query (only caps supportedSearchParams); GET /recent endpoint.
    /// </summary>
    [TestFixture]
    public class GatewayRequestGeneratorFixture : CoreTest<GatewayRequestGenerator>
    {
        private GatewayCapabilities _caps;

        [SetUp]
        public void Setup()
        {
            // Reuse the Plan-01 mixed caps fixture (comix.to enabled+searchable, mangadex
            // enabled+recent-only, disabled.example enabled:false but supportsSearch:true).
            var capsJson = File.ReadAllText("Files/Indexers/Gateway/caps.json");
            _caps = JsonConvert.DeserializeObject<GatewayCapabilities>(capsJson);

            Subject.Capabilities = _caps;
            Subject.Settings = new GatewaySettings
            {
                BaseUrl = "https://gateway.example.com",
                ApiKey = "test-api-key",
                EnabledSources = System.Array.Empty<string>(), // empty = all enabled+searchable
                MultiLanguages = new[] { Language.English.Id }
            };
        }

        private static GatewaySearchRequest BodyOf(IndexerRequest request)
        {
            var json = Encoding.UTF8.GetString(request.HttpRequest.ContentData);
            return JsonConvert.DeserializeObject<GatewaySearchRequest>(json);
        }

        private static Manga.Manga MangaWithTitle(string title) => new Manga.Manga { Title = title };

        [Test]
        public void disabled_source_skipped_no_recordfailure()
        {
            // Select the disabled source explicitly — it must STILL be excluded from sources[].
            Subject.Settings.EnabledSources = new[] { "comix.to", "disabled.example" };

            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();
            var body = BodyOf(request);

            body.Sources.Should().NotContain("disabled.example",
                "an enabled:false caps source is skip-only (D-03) — absent from the queried sources[]");
            body.Sources.Should().Contain("comix.to");

            // D-03 skip-only is STRUCTURAL: the generator must reference NO status service. Assert
            // by reflection that GatewayRequestGenerator has no IIndexerSourceStatusService field.
            typeof(GatewayRequestGenerator)
                .GetFields(System.Reflection.BindingFlags.Instance |
                           System.Reflection.BindingFlags.Public |
                           System.Reflection.BindingFlags.NonPublic)
                .Select(f => f.FieldType)
                .Should().NotContain(typeof(NzbDrone.Core.Indexers.IIndexerSourceStatusService),
                    "the request generator never calls RecordFailure (D-03) — no status service is wired");
        }

        [Test]
        public void search_request_is_post_with_apikey_and_query()
        {
            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();

            request.HttpRequest.Method.Should().Be(HttpMethod.Post);
            request.HttpRequest.Headers["X-Api-Key"].Should().Be("test-api-key");

            var body = BodyOf(request);
            body.Query.Should().NotBeNullOrWhiteSpace();
            body.Query.Should().Contain("Solo Leveling");
            body.Type.Should().Be("manga");
        }

        [Test]
        public void interactive_flag_maps_from_criteria()
        {
            var criteria = new MangaSearchCriteria
            {
                Manga = MangaWithTitle("Solo Leveling"),
                InteractiveSearch = true
            };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();

            BodyOf(request).Interactive.Should().BeTrue();
        }

        [Test]
        public void chapter_criteria_includes_chapter_param()
        {
            var criteria = new ChapterSearchCriteria
            {
                Manga = MangaWithTitle("Solo Leveling"),
                Chapters = new System.Collections.Generic.List<Manga.Chapter>
                {
                    new Manga.Chapter { ChapterNumber = 12.5m }
                }
            };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();
            var body = BodyOf(request);

            body.Type.Should().Be("chapter");
            body.Chapter.Should().Be(12.5m);
        }

        [Test]
        public void emits_only_supported_search_params()
        {
            // caps.json supportedSearchParams = [q, mangadexId, chapter, language, sourceKey];
            // it deliberately OMITS "volume". A volume hint must NOT be emitted, but query ALWAYS is.
            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();
            var body = BodyOf(request);

            // query is always sent (Pitfall 4 — safe fallback).
            body.Query.Should().NotBeNullOrWhiteSpace();

            // "volume" is not advertised → Volume must be null (omitted from emitted params).
            body.Volume.Should().BeNull("volume is not in caps supportedSearchParams; only advertised params emit");
        }

        [Test]
        public void recent_request_targets_recent_endpoint()
        {
            var request = Subject.GetRecentRequests().GetAllTiers().First().First();

            request.HttpRequest.Method.Should().Be(HttpMethod.Get);
            request.Url.FullUri.Should().Contain("/recent");
            request.Url.FullUri.Should().Contain("limit=");
            request.HttpRequest.Headers["X-Api-Key"].Should().Be("test-api-key");
        }

        [Test]
        public void search_uses_caps_default_page_size_when_no_override()
        {
            // caps.json advertises limits.defaultPageSize = 50; with no Settings.ResultLimit override
            // the search body Limit must fall back to that caps default.
            Subject.Settings.ResultLimit = null;

            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();

            BodyOf(request).Limit.Should().Be(_caps.Limits.DefaultPageSize);
        }

        [Test]
        public void search_passes_through_result_limit_override_verbatim()
        {
            // A user override wins over the caps default and is passed through UN-clamped — even when
            // it exceeds limits.maxPageSize (the gateway owns its own ceiling; we do not clamp).
            Subject.Settings.ResultLimit = 250;

            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var request = Subject.GetSearchRequests(criteria).GetAllTiers().First().First();

            BodyOf(request).Limit.Should().Be(250);
        }

        [Test]
        public void recent_uses_result_limit_override()
        {
            Subject.Settings.ResultLimit = 250;

            var request = Subject.GetRecentRequests().GetAllTiers().First().First();

            request.Url.FullUri.Should().Contain("limit=250");
        }

        [Test]
        public void automatic_search_chain_emits_full_coverage_offset_sequence()
        {
            // quick task 260620-ing: AUTOMATIC search (InteractiveSearch=false) emits ONE pageable
            // request that enumerates to a FULL-COVERAGE offset stride (0, L, 2L, …) bounded only by
            // the MaxSearchPages runaway guard. With caps.json (defaultPageSize=50) and no override
            // L=50. The kept FetchReleases engine breaks at the first SHORT page at runtime; here we
            // only assert the emitted sequence shape.
            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var requests = Subject.GetSearchRequests(criteria).GetAllTiers().First().ToList();

            requests.Should().HaveCount(1000, "MaxSearchPages full-coverage runaway guard");

            // offsets advance by the effective limit L=50
            requests.Take(6).Select(r => BodyOf(r).Offset)
                    .Should().Equal(0, 50, 100, 150, 200, 250);

            // Sample the first few pages: each is a POST /search with X-Api-Key and the SAME
            // query/type/limit as page 0 — only Offset advances.
            var page0 = BodyOf(requests.First());
            foreach (var request in requests.Take(5))
            {
                request.HttpRequest.Method.Should().Be(HttpMethod.Post);
                request.Url.FullUri.Should().Contain("/search");
                request.HttpRequest.Headers["X-Api-Key"].Should().Be("test-api-key");

                var body = BodyOf(request);
                body.Query.Should().Be(page0.Query);
                body.Type.Should().Be(page0.Type);
                body.Limit.Should().Be(page0.Limit);
            }
        }

        [Test]
        public void automatic_search_offset_stride_matches_result_limit()
        {
            // The offset stride is keyed to the SAME EffectiveLimit ladder as the page Limit: a 250
            // override (un-clamped — ResultLimit is the per-page pull, NOT clamped to maxPageSize)
            // strides 0, 250, 500, 750, ….
            Subject.Settings.ResultLimit = 250;

            var criteria = new MangaSearchCriteria { Manga = MangaWithTitle("Solo Leveling") };
            var requests = Subject.GetSearchRequests(criteria).GetAllTiers().First().ToList();

            requests.Should().HaveCount(1000, "MaxSearchPages full-coverage runaway guard");
            requests.Take(4).Select(r => BodyOf(r).Offset).Should().Equal(0, 250, 500, 750);
            BodyOf(requests.First()).Limit.Should().Be(250, "ResultLimit is the per-page pull, un-clamped");
        }

        [Test]
        public void interactive_search_chain_emits_single_first_page()
        {
            // quick task 260620-ing: the interactive Search tab shows only the FIRST page — exactly
            // one request at offset 0, never the full-coverage multi-page walk.
            var criteria = new MangaSearchCriteria
            {
                Manga = MangaWithTitle("Solo Leveling"),
                InteractiveSearch = true
            };
            var requests = Subject.GetSearchRequests(criteria).GetAllTiers().First().ToList();

            requests.Should().HaveCount(1, "interactive search is single-page (first page only)");
            BodyOf(requests.First()).Offset.Should().Be(0);
            BodyOf(requests.First()).Interactive.Should().BeTrue();
        }

        [Test]
        public void recent_chain_emits_single_request()
        {
            // /recent is NOT paged — exactly one request regardless of the search paging change.
            Subject.GetRecentRequests().GetAllTiers().First().ToList().Count.Should().Be(1);
        }

        [Test]
        public void empty_effective_recent_sources_yields_no_request()
        {
            // WR-06: with an EMPTY selection AND no recent-capable caps source, EffectiveSources
            // returns an EMPTY (non-null) list. Building an unscoped /recent here would make the
            // gateway interpret the missing `sources` param as "all sources" and keep polling on
            // every RSS tick. The generator must instead return an EMPTY request chain.
            Subject.Capabilities = new GatewayCapabilities
            {
                Sources = new System.Collections.Generic.List<GatewaySourceCap>
                {
                    // enabled + searchable but NOT recent-capable → empty recent effective set.
                    new GatewaySourceCap
                    {
                        Key = "search.only",
                        Name = "Search Only",
                        Enabled = true,
                        SupportsSearch = true,
                        SupportsRecent = false
                    }
                }
            };
            Subject.Settings.EnabledSources = System.Array.Empty<string>(); // empty = "all"

            var chain = Subject.GetRecentRequests();

            chain.GetAllTiers().SelectMany(t => t).Should().BeEmpty(
                "no recent-capable source means the recent chain must be empty (not an unscoped /recent poll)");
        }
    }
}
