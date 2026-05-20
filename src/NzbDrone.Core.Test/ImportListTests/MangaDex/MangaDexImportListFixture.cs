using System;
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
using NzbDrone.Core.ImportLists.MangaDex.Resource;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MangaDex
{
    // Phase 27 Plan 27-02 Task 3 — unit tier for MangaDexImportList.
    //
    // Tests (per 27-02-PLAN.md Task 3 behavior list):
    //   1. start_oauth_password_grant_persists_tokens — D-08 internal-only OAuth flow:
    //      RequestAction("startOAuth") calls proxy.PasswordGrant and persists the token
    //      block on Settings POCO; returns { success: true, authUser, expires }.
    //   2. fetch_paginated_3_pages_terminates_on_total — base FetchItems loop walks
    //      offset = 0/100/200 and breaks on partial page (240 items spread 100/100/40).
    //   3. every_outbound_request_sets_RateLimitKey_mangadex — Pitfall 10 HARD RULE:
    //      every captured HttpRequest.RateLimitKey == "mangadex" (zero exceptions).
    //   4. RefreshToken_uses_grant_type_refresh_token_endpoint — concrete provider
    //      override invokes proxy.RefreshAccessToken; persists rotated tokens with
    //      Trakt.cs:151 null-coalesce on RefreshToken.
    //
    // No live HTTP — Mocker stubs IHttpClient + IMangaDexImportListProxy. Cassettes
    // remain unauthored at this tier (the proxy interface seam already isolates the
    // wire format; cassette tests would belong to a future proxy-level integration
    // fixture).
    [TestFixture]
    public class MangaDexImportListFixture : CoreTest<MangaDexImportList>
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
                Expires = DateTime.UtcNow.AddHours(1) // valid so base.Fetch's lookahead doesn't pre-refresh
            };

            Subject.Definition = new ImportListDefinition
            {
                Id = 1234,
                Name = "MangaDex (fixture)",
                Settings = _settings
            };
        }

        // ── Test 1: D-08 startOAuth persists tokens ──────────────────────────────────
        [Test]
        public void start_oauth_password_grant_persists_tokens()
        {
            _proxy.Setup(p => p.PasswordGrant(It.IsAny<MangaDexImportListSettings>()))
                  .Returns(new MangaDexTokenResponse
                  {
                      AccessToken = "fixture-access-token",
                      RefreshToken = "fixture-refresh-token",
                      ExpiresIn = 900, // 15 minutes
                      TokenType = "Bearer"
                  });

            var result = Subject.RequestAction("startOAuth", new Dictionary<string, string>());

            // Tokens MUST be persisted on the Settings POCO.
            _settings.AccessToken.Should().Be("fixture-access-token");
            _settings.RefreshToken.Should().Be("fixture-refresh-token");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddSeconds(800),
                "Settings.Expires must be ~15 minutes in the future per Keycloak expires_in=900");
            _settings.AuthUser.Should().Be("user-fixture",
                "AuthUser falls back to user-supplied Username (D-08 — MangaDex token endpoint doesn't return a username)");

            // Result envelope mirrors Trakt's `new { OauthUrl = ... }` shape but with
            // success/authUser/expires keys per D-08 internal-only flow.
            result.Should().NotBeNull();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().Contain("\"success\":true");
            json.Should().Contain("\"authUser\":\"user-fixture\"");

            // T-V7 audit — the response envelope must NOT include any token field.
            json.Should().NotContain("fixture-access-token");
            json.Should().NotContain("fixture-refresh-token");
        }

        // ── Test 2: paginated fetch terminates on partial page ───────────────────────
        [Test]
        public void fetch_paginated_3_pages_terminates_on_total()
        {
            // The base FetchItems loop walks pages until either IsFullPage returns false
            // OR cum >= MaxNumResultsPerQuery. The request generator emits 10 page-100
            // requests; the loop should consume 3 pages (100/100/40) and break on the
            // third (partial page).
            var pageIndex = 0;
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req =>
                       {
                           var content = pageIndex == 0
                               ? BuildFollowsJson(start: 0, count: 100)
                               : pageIndex == 1
                                   ? BuildFollowsJson(start: 100, count: 100)
                                   : BuildFollowsJson(start: 200, count: 40);
                           pageIndex++;
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, content, HttpStatusCode.OK);
                       });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            result.AnyFailure.Should().BeFalse();
            result.Manga.Should().HaveCount(240, "3 pages of 100/100/40 items = 240 total; the partial third page must terminate the walk.");
            result.Manga.Select(m => m.MangaDexId).Should().OnlyHaveUniqueItems();
        }

        // ── Test 3: Pitfall 10 HARD RULE — every outbound request uses SourceKey="mangadex" ──
        [Test]
        public void every_outbound_request_sets_RateLimitKey_mangadex()
        {
            var captured = new List<HttpRequest>();
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Callback<HttpRequest>(req => captured.Add(req))
                       .Returns<HttpRequest>(req =>
                       {
                           var body = BuildFollowsJson(start: 0, count: 5);
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, body, HttpStatusCode.OK);
                       });

            Subject.Fetch();

            captured.Should().NotBeEmpty("the base FetchItems loop must dispatch at least one follows request");
            foreach (var req in captured)
            {
                req.RateLimitKey.Should().Be(
                    "mangadex",
                    "Pitfall 10 HARD RULE: SourceKey is SHARED with MangaDexMetadataSource + MangaDexIndexer + in-process downloader; NEVER a sub-bucket.");
            }
        }

        // ── Test 4: RefreshToken hits Keycloak refresh-token grant ───────────────────
        [Test]
        public void RefreshToken_uses_grant_type_refresh_token_endpoint()
        {
            // Pre-condition: token is INSIDE the lookahead window so RefreshTokenIfNecessary
            // triggers RefreshToken(); the proxy returns a rotated AccessToken + a NEW
            // refresh-token (testing the Trakt.cs:151 null-coalesce semantics).
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // 30s < 5min lookahead
            _settings.RefreshToken = "old-refresh-token";
            _settings.AccessToken = "old-access-token";

            _proxy.Setup(p => p.RefreshAccessToken(It.IsAny<MangaDexImportListSettings>()))
                  .Returns(new MangaDexTokenResponse
                  {
                      AccessToken = "new-access-token",
                      RefreshToken = "new-refresh-token",
                      ExpiresIn = 900
                  });

            // Stub Execute so base.Fetch's subsequent follows-call doesn't crash mid-test.
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req =>
                       {
                           var body = BuildFollowsJson(0, 0);
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, body, HttpStatusCode.OK);
                       });

            Subject.Fetch();

            _proxy.Verify(
                p => p.RefreshAccessToken(It.IsAny<MangaDexImportListSettings>()),
                Times.Once,
                "OAuthAwareImportListBase.Fetch() must call RefreshTokenIfNecessary, which calls the per-provider RefreshToken override.");

            _settings.AccessToken.Should().Be("new-access-token", "rotated access-token must be persisted on Settings.");
            _settings.RefreshToken.Should().Be("new-refresh-token", "Trakt.cs:151 null-coalesce: when proxy returns a non-null refresh-token, persist it.");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddSeconds(800), "Expires must be pushed past the lookahead window.");

            // Repository UpdateSettings must be called when Definition.Id > 0.
            _repo.Verify(
                r => r.UpdateSettings(It.IsAny<ImportListDefinition>()),
                Times.Once,
                "rotated tokens must round-trip into the DB so subsequent ImportListSync invocations pick them up.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────
        private static byte[] BuildFollowsJson(int start, int count)
        {
            // Minimal envelope shaped per MangaDexFollowsResource. `data[].id` strings are
            // stable across pages so the dedup assertion in Test 2 sees the right uniques.
            var sb = new StringBuilder();
            sb.Append("{\"result\":\"ok\",\"response\":\"collection\",\"limit\":");
            sb.Append(Math.Max(count, 100));
            sb.Append(",\"offset\":");
            sb.Append(start);
            sb.Append(",\"total\":240,\"data\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"id\":\"id-");
                sb.Append(start + i);
                sb.Append("\",\"type\":\"manga\",\"attributes\":{\"title\":{\"en\":\"Title ");
                sb.Append(start + i);
                sb.Append("\"},\"links\":{},\"year\":2024,\"status\":\"ongoing\",\"contentRating\":\"safe\"},\"relationships\":[]}");
            }

            sb.Append("]}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
