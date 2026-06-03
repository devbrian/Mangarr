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
