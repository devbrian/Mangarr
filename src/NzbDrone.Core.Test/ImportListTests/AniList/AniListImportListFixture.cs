using System;
using System.Collections.Generic;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.ImportLists.AniList.Resource;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests.AniList
{
    // Phase 27 Plan 27-03 Task 3 — unit tier for AniListImportList.
    //
    // Tests (per 27-03-PLAN.md Task 3 behavior list):
    //   1. (implicit) AniListImportList carries Name/ListType/MinRefreshInterval (Plan Test 1).
    //   2. start_oauth_returns_pin_url — RequestAction("startOAuth") returns
    //      { OauthUrl = "https://anilist.co/api/v2/oauth/pin?client_id=...&response_type=code" }
    //      per D-07 (Plan Test 2).
    //   3. get_auth_pin_exchanges_token — RequestAction("getAuthPin", { pin })
    //      calls IAniListImportListProxy.ExchangePinForToken, persists AccessToken + Expires,
    //      returns the public envelope { accessToken, expires, authUser } (Plan Test 3).
    //   4. fetch_uses_shared_AniListGraphQlTransport — spy on IAniListGraphQlTransport.Post,
    //      assert invoked exactly once per Fetch with a body containing MediaListCollection
    //      query (Plan Test 4).
    //   5. status_filter_propagates_to_graphql_variables — when Settings.Status = PLANNING,
    //      the GraphQL body sent to the transport contains "status":"PLANNING" (Plan Test 5).
    //   6. refresh_token_is_noop_for_1year_jwt — RefreshToken() override does not throw and
    //      logs a warning; Fetch() completes normally even with expired Settings.Expires
    //      (Plan Test 6).
    //
    // No live HTTP — Mocker stubs IAniListGraphQlTransport + IHttpClient + IAniListImportListProxy.
    [TestFixture]
    public class AniListImportListFixture : CoreTest<AniListImportList>
    {
        private Mock<IAniListImportListProxy> _proxy;
        private Mock<IAniListGraphQlTransport> _transport;
        private Mock<IHttpClient> _httpClient;
        private Mock<IImportListStatusService> _statusService;
        private Mock<IImportListRepository> _repo;
        private AniListImportListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _proxy = Mocker.GetMock<IAniListImportListProxy>();
            _transport = Mocker.GetMock<IAniListGraphQlTransport>();
            _httpClient = Mocker.GetMock<IHttpClient>();
            _statusService = Mocker.GetMock<IImportListStatusService>();
            _repo = Mocker.GetMock<IImportListRepository>();
            _ = Mocker.GetMock<IConfigService>();
            _ = Mocker.GetMock<IMangaParsingService>();
            _ = Mocker.GetMock<ILocalizationService>();

            _statusService.Setup(s => s.GetBlockedProviders())
                          .Returns(new List<ImportListStatus>());

            _settings = new AniListImportListSettings
            {
                ClientId = "client-id-fixture",
                ClientSecret = "client-secret-fixture",
                Status = AniListListStatus.CURRENT,
                AuthUser = "user-fixture",
                AccessToken = "existing-token-fixture",
                Expires = DateTime.UtcNow.AddDays(180) // half-life of 1-year JWT — outside refresh lookahead
            };

            Subject.Definition = new ImportListDefinition
            {
                Id = 5678,
                Name = "AniList (fixture)",
                Settings = _settings
            };
        }

        // ── Test 1: Provider identity ────────────────────────────────────────────────
        [Test]
        public void provider_carries_canonical_identity()
        {
            Subject.Name.Should().Be("AniList");
            Subject.ListType.Should().Be(ImportListType.AniList);
            Subject.MinRefreshInterval.Should().Be(TimeSpan.FromHours(24));
        }

        // ── Test 2: D-07 startOAuth returns pin URL ──────────────────────────────────
        [Test]
        public void start_oauth_returns_pin_url()
        {
            const string expectedUrl = "https://anilist.co/api/v2/oauth/pin?client_id=client-id-fixture&response_type=code";
            _proxy.Setup(p => p.GetPinAuthorizeUrl(It.IsAny<AniListImportListSettings>()))
                  .Returns(expectedUrl);

            var result = Subject.RequestAction("startOAuth", new Dictionary<string, string>());

            result.Should().NotBeNull();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().Contain("OauthUrl");
            json.Should().Contain("anilist.co/api/v2/oauth/pin");
            json.Should().Contain("client_id=client-id-fixture");
            json.Should().Contain("response_type=code");
        }

        // ── Test 3: D-07 getAuthPin exchanges + persists tokens ──────────────────────
        [Test]
        public void get_auth_pin_exchanges_token()
        {
            _proxy.Setup(p => p.ExchangePinForToken("the-paste-pin", It.IsAny<AniListImportListSettings>()))
                  .Returns(new AniListTokenResponse
                  {
                      AccessToken = "fresh-anilist-token",
                      ExpiresIn = 31536000, // ~1 year
                      TokenType = "Bearer"
                  });

            // The provider's AuthUser-resolution follow-up calls _httpClient.Post with a
            // Viewer { name } GraphQL body — stub it to return a username so the
            // Settings.AuthUser persistence path is exercised.
            var viewerJson = "{\"data\":{\"Viewer\":{\"name\":\"resolved-anilist-username\"}}}";
            _httpClient.Setup(c => c.Post(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req => new HttpResponse(
                           req,
                           new HttpHeader { ContentType = "application/json" },
                           viewerJson,
                           HttpStatusCode.OK));

            var query = new Dictionary<string, string> { { "pin", "the-paste-pin" } };
            var result = Subject.RequestAction("getAuthPin", query);

            // Tokens persisted on Settings POCO.
            _settings.AccessToken.Should().Be("fresh-anilist-token");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddDays(360),
                "1-year JWT lifetime — expires_in=31536000 seconds rounds to ~365 days");
            _settings.AuthUser.Should().Be("resolved-anilist-username",
                "AuthUser is resolved via Viewer { name } follow-up after the pin exchange");

            // Public response envelope shape (Trakt.cs:113-122 adapted for AniList).
            result.Should().NotBeNull();
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            json.Should().Contain("accessToken");
            json.Should().Contain("authUser");
            json.Should().Contain("resolved-anilist-username");
        }

        // ── Test 4: Fetch routes through SHARED IAniListGraphQlTransport ─────────────
        [Test]
        public void fetch_uses_shared_AniListGraphQlTransport()
        {
            var capturedBodies = new List<string>();
            _transport.Setup(t => t.Post<AniListMediaListResource>(It.IsAny<string>()))
                      .Callback<string>(body => capturedBodies.Add(body))
                      .Returns(new AniListGraphQlResponse<AniListMediaListResource>
                      {
                          Data = new AniListMediaListResource
                          {
                              MediaListCollection = new AniListMediaListCollection
                              {
                                  Lists = new List<AniListMediaList>
                                  {
                                      new()
                                      {
                                          Entries = new List<AniListMediaListEntry>
                                          {
                                              new()
                                              {
                                                  Media = new AniListMediaListMedia
                                                  {
                                                      Id = 30002,
                                                      IdMal = 13,
                                                      Title = new AniListMediaListTitle { Romaji = "Test", English = "Test" }
                                                  }
                                              }
                                          }
                                      }
                                  }
                              }
                          }
                      });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            _transport.Verify(
                t => t.Post<AniListMediaListResource>(It.IsAny<string>()),
                Times.Once,
                "AniListImportList.FetchImportListResponse must route through the SHARED IAniListGraphQlTransport (Phase 26 Plan 26-02); a parallel GraphQL transport at the ImportList tier is forbidden.");

            capturedBodies.Should().HaveCount(1);
            capturedBodies[0].Should().Contain("MediaListCollection",
                "the GraphQL body must carry the MediaListCollection query verbatim per RESEARCH §Example 3");
            capturedBodies[0].Should().Contain("type: MANGA",
                "the GraphQL query must filter to MANGA media type only — AniList serves anime + manga from the same endpoint");
        }

        // ── Test 5: D-10 status filter propagates to GraphQL variables ───────────────
        [Test]
        public void status_filter_propagates_to_graphql_variables()
        {
            _settings.Status = AniListListStatus.PLANNING;

            string capturedBody = null;
            _transport.Setup(t => t.Post<AniListMediaListResource>(It.IsAny<string>()))
                      .Callback<string>(body => capturedBody = body)
                      .Returns(new AniListGraphQlResponse<AniListMediaListResource>
                      {
                          Data = new AniListMediaListResource
                          {
                              MediaListCollection = new AniListMediaListCollection { Lists = new List<AniListMediaList>() }
                          }
                      });

            Subject.Fetch();

            capturedBody.Should().NotBeNull();

            // Json.ToJson(...) uses formatted output by default (Newtonsoft Indented
            // formatting), so the serialized body contains `"status": "PLANNING"` with a
            // space after the colon. Strip ALL whitespace before substring assertion so the
            // test is robust against Newtonsoft formatting changes.
            var packed = System.Text.RegularExpressions.Regex.Replace(capturedBody, @"\s+", string.Empty);
            packed.Should().Contain("\"status\":\"PLANNING\"",
                "D-10 single-select Status enum (AniListListStatus.PLANNING) must serialize to UPPERCASE string in the GraphQL $status variable");
            packed.Should().Contain("\"userName\":\"user-fixture\"",
                "Settings.AuthUser must flow into the $userName GraphQL variable");
        }

        // ── Test 6: RefreshToken is a no-op for 1-year JWT ───────────────────────────
        [Test]
        public void refresh_token_is_noop_for_1year_jwt()
        {
            // Pre-condition: Settings.Expires is INSIDE the refresh lookahead so the base's
            // RefreshTokenIfNecessary template would normally invoke RefreshToken(). AniList's
            // override is a no-op — Fetch() must complete normally without throwing or
            // attempting any refresh-token-grant HTTP call.
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // 30s < 5min lookahead

            _transport.Setup(t => t.Post<AniListMediaListResource>(It.IsAny<string>()))
                      .Returns(new AniListGraphQlResponse<AniListMediaListResource>
                      {
                          Data = new AniListMediaListResource
                          {
                              MediaListCollection = new AniListMediaListCollection { Lists = new List<AniListMediaList>() }
                          }
                      });

            Action act = () => Subject.Fetch();
            act.Should().NotThrow(
                "AniList tokens are 1-year JWTs with no refresh-token grant; RefreshToken() must be a no-op so the base Fetch() pre-call refresh check completes cleanly even when Settings.Expires is inside the lookahead window.");

            // No proxy refresh call should have been issued — IAniListImportListProxy doesn't
            // declare RefreshAccessToken at all (intentional; AniList omits the grant).
            _proxy.Verify(
                p => p.ExchangePinForToken(It.IsAny<string>(), It.IsAny<AniListImportListSettings>()),
                Times.Never,
                "RefreshToken() must NOT invoke any pin/token endpoint — re-auth is a user-driven re-run of the pin flow (D-07).");

            // RefreshToken() logs one warning by design — explicitly expected so the
            // ExceptionVerification TearDown doesn't fail on the "0 Warn(s) expected but 1
            // logged" assertion (the noop log is documented in CONTEXT lines 28-29 + the
            // test's name itself).
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
