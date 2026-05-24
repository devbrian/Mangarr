using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeList
{
    // Phase 31 Plan 31-02 D-08 (IL2-05) — MAL paging.next cursor-walk behavior fixture.
    //
    // MAL emits opaque server-issued cursor URLs in paging.next. The substrate's offset-
    // style HttpImportListBase.IsFullPage early-exit doesn't apply (D-08 — no Sonarr
    // precedent for cursor-paginated providers in the preserved slice). Plan 31-02 Task 4
    // overrides MalImportList.FetchPage to walk the cursor chain per-response.
    //
    // What this fixture verifies (3 tests, one per behavior axis):
    //   (a) large_list_walks_paging_next_cursor_chain — 3 chained cassette responses
    //       (page 1 + 2 carry paging.next; page 3 carries paging.next=null); assert
    //       releases.Count == 2500 (3 pages × ~833) AND httpClient called 3 times.
    //   (b) untrusted_cursor_host_throws — cassette returns paging.next pointing at an
    //       attacker host (T-V13 defense); assert InvalidOperationException.
    //   (c) cursor_walk_terminates_at_MaxCursorPages_safety_cap — cassette returns a
    //       self-referencing paging.next (always non-null); assert exactly 10 HTTP
    //       requests before terminate (defensive against upstream bug; A1 per RESEARCH).
    //
    // RED before Task 4 lands: the FetchPage override doesn't exist; MAL provider only
    // ever fires the initial GET (single page; 1000-item cap silently truncates large
    // lists per GH #223).
    //
    // GREEN after Task 4 lands: FetchPage walks the chain with MaxCursorPages=10 safety
    // + IsTrustedMalCursor host pin; all 3 [Test] methods pass.
    [TestFixture]
    public class MalCursorWalkFixture : CoreTest<MalImportList>
    {
        private Mock<IMalImportListProxy> _proxy;
        private Mock<IHttpClient> _httpClient;
        private Mock<IImportListStatusService> _statusService;
        private Mock<IImportListRepository> _repo;
        private MalImportListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _proxy = Mocker.GetMock<IMalImportListProxy>();
            _httpClient = Mocker.GetMock<IHttpClient>();
            _statusService = Mocker.GetMock<IImportListStatusService>();
            _repo = Mocker.GetMock<IImportListRepository>();
            _ = Mocker.GetMock<IConfigService>();
            _ = Mocker.GetMock<IMangaParsingService>();
            _ = Mocker.GetMock<ILocalizationService>();

            _statusService.Setup(s => s.GetBlockedProviders())
                          .Returns(new List<ImportListStatus>());

            _settings = new MalImportListSettings
            {
                ClientId = "fixture-client-id",
                Status = MalListStatus.Reading,
                AccessToken = "fixture-access-token",
                RefreshToken = "fixture-refresh-token",
                Expires = DateTime.UtcNow.AddDays(20) // valid; outside refresh lookahead
            };

            Subject.Definition = new ImportListDefinition
            {
                Id = 9013,
                Name = "MyAnimeList (cursor-walk fixture)",
                Settings = _settings
            };
        }

        // ── Test (a): 3 chained pages with paging.next walked end-to-end ──────────────
        [Test]
        public void large_list_walks_paging_next_cursor_chain()
        {
            // Page 1: 833 items + paging.next → canonical MAL API URL with offset=1000
            // Page 2: 833 items + paging.next → canonical MAL API URL with offset=2000
            // Page 3: 834 items + paging.next=null → walk terminates
            // Total: 2500 items in 3 HTTP requests.
            const string canonicalCursor1 = "https://api.myanimelist.net/v2/users/@me/mangalist?offset=1000&limit=1000";
            const string canonicalCursor2 = "https://api.myanimelist.net/v2/users/@me/mangalist?offset=2000&limit=1000";

            var callCount = 0;
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Callback<HttpRequest>(_ => callCount++)
                       .Returns<HttpRequest>(req =>
                       {
                           byte[] content;
                           switch (callCount)
                           {
                               case 1:
                                   content = BuildMalJson(start: 0, count: 833, nextCursor: canonicalCursor1);
                                   break;
                               case 2:
                                   content = BuildMalJson(start: 833, count: 833, nextCursor: canonicalCursor2);
                                   break;
                               default:
                                   content = BuildMalJson(start: 1666, count: 834, nextCursor: null);
                                   break;
                           }

                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, content, HttpStatusCode.OK);
                       });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            result.AnyFailure.Should().BeFalse();
            result.Manga.Should().HaveCount(
                2500,
                "D-08 IL2-05: MalImportList.FetchPage override must walk paging.next so a 3-page MAL list " +
                "(833/833/834 = 2500 items) is fully aggregated. The current shape (Task 4 RED) only fires " +
                "the initial request and truncates at PageSize=1000 per GH #223.");

            callCount.Should().Be(
                3,
                "exactly 3 HTTP requests must fire: page 1 (initial), page 2 (paging.next from page 1), " +
                "page 3 (paging.next from page 2). Page 3's paging.next=null terminates the walk.");

            _httpClient.Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        // ── Test (b): untrusted cursor host throws (T-V13 defense) ────────────────────
        [Test]
        public void untrusted_cursor_host_throws()
        {
            // Initial response carries a malicious paging.next pointing at attacker.example.com.
            // The IsTrustedMalCursor host-pin defense (mirror of MalImportListProxy.cs:255-268)
            // MUST throw InvalidOperationException BEFORE the bearer-token-bearing request fires.
            const string maliciousCursor = "https://attacker.example.com/?token=leak";

            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req =>
                       {
                           var content = BuildMalJson(start: 0, count: 833, nextCursor: maliciousCursor);
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, content, HttpStatusCode.OK);
                       });

            // The base HttpImportListBase.FetchItems wraps the inner foreach in a catch-Exception
            // ladder that swallows the InvalidOperationException and records the failure (line 174-179).
            // We assert via the ImportListFetchResult.AnyFailure flag + no items aggregated, which
            // proves the throw fired before any cursor-follow request leaked the Bearer token.
            //
            // Per RESEARCH §IL2-05 Test Map: "Assert Assert.Throws<InvalidOperationException>(() =>
            // Subject.Fetch()) or FluentAssertions equivalent." We use the failure-flag pathway
            // because the base wraps the exception — but the SUBSTANTIVE assertion is identical:
            // the cursor was rejected, no follow-up Bearer-bearing request fired against
            // attacker.example.com.
            var result = Subject.Fetch();

            result.Should().NotBeNull();
            result.AnyFailure.Should().BeTrue(
                "T-V13 cursor-host defense: when paging.next is not on api.myanimelist.net or not HTTPS, " +
                "MalImportList.FetchPage MUST throw InvalidOperationException to prevent the user's Bearer " +
                "token from leaking to an attacker-controlled host. The base FetchItems Exception ladder " +
                "catches the throw and records the failure — proving the rejection fired upstream of any " +
                "Bearer-bearing GET against the malicious URL.");

            // Exactly 1 HTTP call — the INITIAL request only. The malicious paging.next MUST NOT have
            // been followed. If this assertion fails (2+ calls), the host-pin defense is bypassed.
            _httpClient.Verify(
                c => c.Execute(It.IsAny<HttpRequest>()),
                Times.Once,
                "T-V13: exactly 1 outbound HTTP call (the initial); the malicious paging.next cursor " +
                "MUST NOT have been followed.");
        }

        // ── Test (c): MaxCursorPages safety cap terminates self-referencing walk ──────
        [Test]
        public void cursor_walk_terminates_at_MaxCursorPages_safety_cap()
        {
            // Cassette returns paging.next pointing at a canonical MAL URL FOREVER (self-
            // referencing — upstream bug simulation). Walk must terminate at exactly 10
            // HTTP requests per the MaxCursorPages=10 defensive bound (A1 per RESEARCH).
            const string selfRefCursor = "https://api.myanimelist.net/v2/users/@me/mangalist?offset=1000&limit=1000";

            var callCount = 0;
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Callback<HttpRequest>(_ => callCount++)
                       .Returns<HttpRequest>(req =>
                       {
                           // Every response carries paging.next = selfRefCursor → walk never
                           // self-terminates via null-check. The MaxCursorPages=10 safety
                           // bound is what stops it.
                           var content = BuildMalJson(start: 0, count: 5, nextCursor: selfRefCursor);
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, content, HttpStatusCode.OK);
                       });

            Subject.Fetch();

            callCount.Should().Be(
                10,
                "MaxCursorPages=10 safety cap: with paging.next always non-null + always on the trusted " +
                "host, the walk MUST terminate after exactly 10 HTTP requests (defensive against upstream " +
                "self-referencing paging.next bug). RESEARCH A1: bound chosen so 1000-item pages × 10 pages " +
                "= 10000 list items max; users with >10k followed manga get a truncated read rather than " +
                "an infinite loop.");

            _httpClient.Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Exactly(10));
        }

        // ── Helper: minimal MAL /v2/users/@me/mangalist JSON envelope ─────────────────
        private static byte[] BuildMalJson(int start, int count, string nextCursor)
        {
            var sb = new StringBuilder();
            sb.Append("{\"data\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var malId = start + i + 1; // MAL ids start at 1, never 0
                sb.Append("{\"node\":{\"id\":");
                sb.Append(malId);
                sb.Append(",\"title\":\"MalCursorWalk Manga ");
                sb.Append(malId);
                sb.Append("\"},\"list_status\":{\"status\":\"reading\",\"score\":0,\"num_chapters_read\":0,\"is_rereading\":false,\"updated_at\":\"2024-01-01T00:00:00+00:00\"}}");
            }

            sb.Append("],\"paging\":");
            if (string.IsNullOrEmpty(nextCursor))
            {
                sb.Append("{}");
            }
            else
            {
                sb.Append("{\"next\":\"");
                sb.Append(nextCursor);
                sb.Append("\"}");
            }

            sb.Append('}');
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
