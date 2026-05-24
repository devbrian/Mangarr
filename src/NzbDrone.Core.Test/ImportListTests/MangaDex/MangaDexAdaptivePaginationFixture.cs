using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MangaDex;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MangaDex
{
    // Phase 31 Plan 31-02 D-07 (IL2-02) — adaptive pagination behavior fixture.
    //
    // What this fixture verifies: when MangaDex returns a partial page (fewer items than
    // PageSize=100), the substrate's HttpImportListBase.IsFullPage default impl
    // (PageSize != 0 && page.Count >= PageSize at HttpImportListBase.cs:203-206) returns
    // false, which triggers the inner-loop break at HttpImportListBase.cs:88-96 — and the
    // walk terminates on the FIRST partial page rather than firing all 10 offset-stepped
    // requests.
    //
    // RED before Task 2 lands:
    //   * Current MangaDexImportList lacks the `public override int PageSize => 100;`
    //     override → inherits PageSize=0 default → IsFullPage always returns false →
    //     break ALWAYS fires on page 1 (correct only by coincidence).
    //   * Current MangaDexImportListRequestGenerator calls chain.Add(new[] { request })
    //     10 separate times → each ends up in its own PageableRequest → inner foreach has
    //     a single-element collection to iterate → break is meaningless → all 10 dispatch.
    //
    // GREEN after Task 2 lands:
    //   * PageSize=100 override on MangaDexImportList → IsFullPage default impl evaluates
    //     correctly.
    //   * Single chain.Add(IEnumerable) with 10 offset-stepped requests in
    //     MangaDexImportListRequestGenerator → inner foreach iterates all 10 in ONE
    //     PageableRequest → first partial page triggers break → only 1 HTTP request fires.
    [TestFixture]
    public class MangaDexAdaptivePaginationFixture : CoreTest<MangaDexImportList>
    {
        private Mock<IMangaDexImportListProxy> _proxy;
        private Mock<IHttpClient> _httpClient;
        private Mock<IImportListStatusService> _statusService;
        private Mock<IImportListRepository> _repo;
        private MangaDexImportListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _proxy = Mocker.GetMock<IMangaDexImportListProxy>();
            _httpClient = Mocker.GetMock<IHttpClient>();
            _statusService = Mocker.GetMock<IImportListStatusService>();
            _repo = Mocker.GetMock<IImportListRepository>();
            _ = Mocker.GetMock<IConfigService>();
            _ = Mocker.GetMock<IMangaParsingService>();
            _ = Mocker.GetMock<ILocalizationService>();

            _statusService.Setup(s => s.GetBlockedProviders())
                          .Returns(new List<ImportListStatus>());

            _settings = new MangaDexImportListSettings
            {
                ClientId = "client-id-fixture",
                ClientSecret = "client-secret-fixture",
                Username = "user-fixture",
                Password = "pass-fixture",
                AccessToken = "fixture-access-token",
                RefreshToken = "fixture-refresh-token",
                Expires = System.DateTime.UtcNow.AddHours(1)
            };

            Subject.Definition = new ImportListDefinition
            {
                Id = 1234,
                Name = "MangaDex (adaptive-pagination fixture)",
                Settings = _settings
            };
        }

        // ── Test: 50-item cassette triggers exactly 1 HTTP request ────────────────────
        //
        // Per RESEARCH §IL2-02 cassette verify-fixture acceptance: a 50-item MangaDex
        // follows cassette MUST trigger exactly 1 HTTP request (FetchPage called once;
        // partial-page → break fires on first page → inner-loop terminates).
        [Test]
        public void fetch_50_item_follows_list_triggers_exactly_one_http_request()
        {
            var callCount = 0;
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Callback<HttpRequest>(_ => callCount++)
                       .Returns<HttpRequest>(req =>
                       {
                           // Return a partial page (50 < PageSize=100) — IsFullPage must
                           // return false and break the inner loop.
                           var content = BuildFollowsJson(start: 0, count: 50);
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, content, HttpStatusCode.OK);
                       });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            result.AnyFailure.Should().BeFalse();
            result.Manga.Should().HaveCount(50, "the partial 50-item page must yield all 50 items but stop the walk.");
            result.Manga.Select(m => m.MangaDexId).Should().OnlyHaveUniqueItems();

            // STATE assertion per RESEARCH §Item 1: exactly ONE HTTP request — NOT 10.
            // This is the load-bearing assertion that validates D-07. The current shape
            // (PageSize=0 + 10 chain.Add) fires 10 calls; the target shape (PageSize=100
            // + single chain.Add(IEnumerable)) fires 1.
            callCount.Should().Be(
                1,
                "D-07 IL2-02: with PageSize=100 override + single chain.Add(IEnumerable), the substrate's " +
                "IsFullPage early-exit must terminate the walk after the FIRST partial page (50 < 100). " +
                "The current broken shape (PageSize=0 + 10 separate chain.Add calls) fires 10 requests because " +
                "(a) IsFullPage default impl returns false when PageSize=0 [HttpImportListBase.cs:203-206], and " +
                "(b) each chain.Add(new[]{ request }) creates a single-element PageableRequest, so the inner-loop " +
                "break at HttpImportListBase.cs:88-96 has nothing to break out of.");

            _httpClient.Verify(
                c => c.Execute(It.IsAny<HttpRequest>()),
                Times.Once,
                "Times.Once assertion redundant with callCount but pins the contract explicitly via Moq.");
        }

        // ── Helper: minimal MangaDex /user/follows/manga JSON envelope ────────────────
        private static byte[] BuildFollowsJson(int start, int count)
        {
            var sb = new StringBuilder();
            sb.Append("{\"result\":\"ok\",\"response\":\"collection\",\"limit\":");
            sb.Append(System.Math.Max(count, 100));
            sb.Append(",\"offset\":");
            sb.Append(start);
            sb.Append(",\"total\":");
            sb.Append(count);
            sb.Append(",\"data\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"id\":\"adp-id-");
                sb.Append(start + i);
                sb.Append("\",\"type\":\"manga\",\"attributes\":{\"title\":{\"en\":\"Adaptive Title ");
                sb.Append(start + i);
                sb.Append("\"},\"links\":{},\"year\":2024,\"status\":\"ongoing\",\"contentRating\":\"safe\"},\"relationships\":[]}");
            }

            sb.Append("]}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
