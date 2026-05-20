using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.ImportLists.MyAnimeList.Resource;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 3 — unit tier for MalImportList.
    //
    // Tests (per 27-04-PLAN.md Task 3 behavior list):
    //   1. provider_carries_canonical_identity — Name/ListType/MinRefreshInterval (Test 1).
    //   2. start_oauth_returns_authorize_url_with_pkce_plain_and_state — generates fresh
    //      PKCE state, persists Settings.PendingPkceState JSON blob, returns OauthUrl with
    //      code_challenge_method=plain + state nonce (Test 2).
    //   3. get_oauth_token_validates_state_then_exchanges — happy path: parses redirected
    //      URL, validates state, exchanges code+verifier, persists tokens, CLEARS
    //      PendingPkceState (Test 3 — single-use enforcement).
    //   4. state_validation_rejects_mismatched_nonce — presented state != persisted nonce
    //      ⇒ rejection without exchange; PendingPkceState UNCHANGED (Test 4).
    //   5. state_validation_rejects_expired_nonce — presented when ExpiresAt < UtcNow
    //      ⇒ rejection; PendingPkceState CLEARED (Test 5).
    //   6. RefreshToken_uses_refresh_grant_with_null_coalesce — refresh-token grant
    //      persists rotated AccessToken + applies Trakt.cs:151 null-coalesce on
    //      RefreshToken (Test 6).
    //   7. concurrent_refresh_invocations_serialize_via_semaphore — 8 parallel Fetch()
    //      calls with stale Expires ⇒ exactly ONE RefreshAccessToken proxy call
    //      (D-05 + Pitfall 9 mitigation; inherited from OAuthAwareImportListBase
    //      Plan 27-01) (Test 7).
    //   8. fetch_401_force_refresh_then_retry — initial 401 ⇒ force-expire Expires,
    //      refresh, retry once (Test 8 — D-05 reactive 401-retry decorator).
    //
    // No live HTTP — Mocker stubs IMalImportListProxy + IHttpClient + IImportListRepository.
    [TestFixture]
    public class MalImportListFixture : CoreTest<MalImportList>
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
                Id = 9012,
                Name = "MyAnimeList (fixture)",
                Settings = _settings
            };
        }

        // ── Test 1: Provider identity ────────────────────────────────────────────────
        [Test]
        public void provider_carries_canonical_identity()
        {
            Subject.Name.Should().Be("MyAnimeList");
            Subject.ListType.Should().Be(ImportListType.MyAnimeList);
            Subject.MinRefreshInterval.Should().Be(TimeSpan.FromHours(24));
        }

        // ── Test 2: D-09 startOAuth returns authorize URL with PKCE-plain + state ─────
        [Test]
        public void start_oauth_returns_authorize_url_with_pkce_plain_and_state()
        {
            _proxy.Setup(p => p.BuildAuthorizeUrl(It.IsAny<string>(), It.IsAny<MalOAuthState>()))
                  .Returns<string, MalOAuthState>((clientId, state) =>
                      $"https://myanimelist.net/v1/oauth2/authorize?response_type=code&client_id={clientId}&code_challenge={state.Verifier}&code_challenge_method=plain&state={state.StateNonce}&redirect_uri=https://mangarr.local/oauth/mal/callback");

            var result = Subject.RequestAction("startOAuth", new Dictionary<string, string>());

            // PendingPkceState must be populated with a serialized MalOAuthState blob.
            _settings.PendingPkceState.Should().NotBeNullOrEmpty(
                "startOAuth must persist the fresh PKCE state (Verifier + StateNonce + ExpiresAt) as a JSON blob on Settings.PendingPkceState per Discretion #2 shape (a).");

            var persisted = JsonConvert.DeserializeObject<MalOAuthState>(_settings.PendingPkceState);
            persisted.Should().NotBeNull();
            persisted.StateNonce.Should().NotBeNullOrEmpty();
            persisted.Verifier.Should().NotBeNullOrEmpty();
            persisted.ExpiresAt.Should().BeAfter(DateTime.UtcNow,
                "ExpiresAt must be in the future (10-min TTL per MalConstants.StateTtl).");

            // Return envelope shape mirrors Trakt's { OauthUrl = ... } — the FE useOAuth
            // hook reads OauthUrl verbatim and window.opens it.
            result.Should().NotBeNull();
            var json = JsonConvert.SerializeObject(result);
            json.Should().Contain("OauthUrl");
            json.Should().Contain("code_challenge_method=plain",
                "MAL only accepts PKCE-plain; the authorize URL must encode that explicitly.");
            json.Should().Contain($"state={persisted.StateNonce}",
                "the state query parameter must be the freshly-issued CSRF nonce.");
        }

        // ── Test 3: D-09 getOAuthToken happy path — validates + exchanges + clears ────
        [Test]
        public void get_oauth_token_validates_state_then_exchanges()
        {
            // Pre-populate PendingPkceState (as if startOAuth had just run).
            var pending = MalOAuthState.Create();
            _settings.PendingPkceState = JsonConvert.SerializeObject(pending);

            _proxy.Setup(p => p.ExchangeCodeForToken("fixture-client-id", "auth-code-fixture", pending.Verifier))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "fresh-mal-token",
                      RefreshToken = "fresh-mal-refresh",
                      ExpiresIn = 2592000, // 30 days
                      TokenType = "Bearer"
                  });

            // Compose the redirected URL exactly as MAL would deliver it.
            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            // Tokens persisted on Settings.
            _settings.AccessToken.Should().Be("fresh-mal-token");
            _settings.RefreshToken.Should().Be("fresh-mal-refresh");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddDays(29),
                "MAL issues ~30-day access tokens (expires_in=2,592,000 seconds).");

            // Single-use enforcement: PendingPkceState MUST be cleared.
            _settings.PendingPkceState.Should().BeNull(
                "T-V11 single-use: Settings.PendingPkceState must be cleared on first successful exchange so a replay of the same callback URL fails.");

            // Repository UpdateSettings called for the persistent token block.
            _repo.Verify(
                r => r.UpdateSettings(It.IsAny<ImportListDefinition>()),
                Times.Once,
                "tokens must round-trip into the DB so subsequent ImportListSync invocations pick them up.");

            // T-V7: response envelope must NOT echo the refresh token.
            result.Should().NotBeNull();
            var json = JsonConvert.SerializeObject(result);
            json.Should().Contain("accessToken");
            json.Should().NotContain("fresh-mal-refresh",
                "T-V7: the response envelope from getOAuthToken must NEVER echo the refresh token (FE only needs the access token + expires).");
        }

        // ── Test 4: T-V11 CSRF — mismatched state nonce is rejected ───────────────────
        [Test]
        public void state_validation_rejects_mismatched_nonce()
        {
            var pending = MalOAuthState.Create();
            _settings.PendingPkceState = JsonConvert.SerializeObject(pending);

            // Attacker swaps the state nonce while keeping the code.
            var maliciousUrl = "https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state=ATTACKER_FORGED_NONCE";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", maliciousUrl } });

            // Tokens MUST NOT be exchanged.
            _proxy.Verify(
                p => p.ExchangeCodeForToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never,
                "T-V11 CSRF: mismatched state nonce must reject the exchange before contacting MAL.");

            // PendingPkceState UNCHANGED — re-try within TTL is allowed.
            _settings.PendingPkceState.Should().NotBeNullOrEmpty(
                "PendingPkceState must remain on rejected exchange so the legitimate user can retry within the TTL window.");

            var json = JsonConvert.SerializeObject(result);
            json.Should().Contain("InvalidState",
                "rejection envelope must surface the InvalidState error code for the FE banner.");
        }

        // ── Test 5: T-V11 Replay — expired state nonce is rejected ────────────────────
        [Test]
        public void state_validation_rejects_expired_nonce()
        {
            var pending = MalOAuthState.Create();
            pending.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); // already expired
            _settings.PendingPkceState = JsonConvert.SerializeObject(pending);

            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            _proxy.Verify(
                p => p.ExchangeCodeForToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never,
                "TTL-expired state nonce must reject the exchange.");

            _settings.PendingPkceState.Should().BeNull(
                "expired PendingPkceState must be cleared so the FE forces a fresh startOAuth flow.");

            var json = JsonConvert.SerializeObject(result);
            json.Should().Contain("StateExpired",
                "rejection envelope must surface the StateExpired error code for the FE banner.");
        }

        // ── Test 6: RefreshToken applies Trakt.cs:151 null-coalesce ───────────────────
        [Test]
        public void RefreshToken_uses_refresh_grant_with_null_coalesce()
        {
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // inside 5-min lookahead
            _settings.AccessToken = "old-access";
            _settings.RefreshToken = "old-refresh";

            _proxy.Setup(p => p.RefreshAccessToken("fixture-client-id", "old-refresh"))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "rotated-access",
                      RefreshToken = "rotated-refresh",
                      ExpiresIn = 2592000
                  });

            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req => new HttpResponse(
                           req,
                           new HttpHeader { ContentType = "application/json" },
                           "{\"data\":[],\"paging\":{}}",
                           HttpStatusCode.OK));

            Subject.Fetch();

            _proxy.Verify(
                p => p.RefreshAccessToken("fixture-client-id", "old-refresh"),
                Times.Once,
                "OAuthAwareImportListBase.Fetch() must call RefreshTokenIfNecessary, which calls the per-provider RefreshToken override.");

            _settings.AccessToken.Should().Be("rotated-access",
                "rotated access-token must be persisted on Settings.");
            _settings.RefreshToken.Should().Be("rotated-refresh",
                "Trakt.cs:151 null-coalesce: when MAL returns a rotated refresh token, persist it.");
            _settings.Expires.Should().BeAfter(DateTime.UtcNow.AddDays(29),
                "Expires must be pushed past the lookahead window.");

            _repo.Verify(
                r => r.UpdateSettings(It.IsAny<ImportListDefinition>()),
                Times.Once,
                "rotated tokens must round-trip into the DB.");
        }

        // ── Test 7: D-05 + Pitfall 9 — concurrent refresh serialization ───────────────
        [Test]
        public void concurrent_refresh_invocations_serialize_via_semaphore()
        {
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // inside lookahead

            // Proxy refresh bumps Expires past the lookahead via the provider override,
            // so the in-lock re-check (peer-flow defense) collapses callers 2..8 into
            // no-ops. Exactly ONE refresh call must reach the proxy.
            _proxy.Setup(p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "rotated-access",
                      RefreshToken = "rotated-refresh",
                      ExpiresIn = 2592000
                  });

            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req => new HttpResponse(
                           req,
                           new HttpHeader { ContentType = "application/json" },
                           "{\"data\":[],\"paging\":{}}",
                           HttpStatusCode.OK));

            Parallel.For(0, 8, _ => Subject.Fetch());

            _proxy.Verify(
                p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>()),
                Times.Once,
                "D-05 + Pitfall 9: per-ImportList SemaphoreSlim must serialize concurrent refreshes, " +
                "and the in-lock re-check must collapse callers 2..8 into no-ops so MAL's refresh-token " +
                "rotation endpoint does NOT see a concurrent-grant race (400 invalid_grant cascade).");
        }

        // ── Test 8: D-05 reactive 401-retry decorator ─────────────────────────────────
        [Test]
        public void fetch_401_force_refresh_then_retry()
        {
            _settings.Expires = DateTime.UtcNow.AddDays(20); // valid; lookahead does NOT fire

            // First Execute returns 401; second Execute returns OK. The provider's
            // 401-retry decorator catches the 401, force-expires Settings.Expires,
            // calls RefreshTokenIfNecessary, retries the fetch.
            var callCount = 0;
            _httpClient.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                       .Returns<HttpRequest>(req =>
                       {
                           callCount++;
                           if (callCount == 1)
                           {
                               // base.Fetch's exception ladder wraps HttpException(401) — surface it.
                               throw new HttpException(
                                   req,
                                   new HttpResponse(req, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.Unauthorized));
                           }

                           var body = Encoding.UTF8.GetBytes("{\"data\":[],\"paging\":{}}");
                           return new HttpResponse(req, new HttpHeader { ContentType = "application/json" }, body, HttpStatusCode.OK);
                       });

            _proxy.Setup(p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "post-401-access",
                      RefreshToken = "post-401-refresh",
                      ExpiresIn = 2592000
                  });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            _proxy.Verify(
                p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>()),
                Times.Once,
                "D-05 reactive 401-retry: after the initial 401, the provider must force-expire Settings.Expires and call RefreshTokenIfNecessary which dispatches RefreshAccessToken.");

            _settings.AccessToken.Should().Be("post-401-access",
                "the post-401 refresh must persist the new access token.");
        }
    }
}
