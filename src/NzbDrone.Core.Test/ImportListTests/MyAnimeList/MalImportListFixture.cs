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
    // Tests (per 27-04-PLAN.md Task 3 behavior list + GH #233 client_secret coverage):
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
    //   9. get_oauth_token_reloads_pending_pkce_state_from_repository — GH #231
    //      (2026-05-21 live smoke): when the FE round-trip wipes Settings.PendingPkceState
    //      (SchemaBuilder.ReadFromSchema preserve-existing branch is gated on
    //      Privacy=Password and PendingPkceState is Hidden-without-Privacy), the provider
    //      reloads the DB-persisted blob via _importListRepository.Get before the
    //      empty-check fires, so the validation proceeds to the exchange (Test 9 — Option
    //      A per-provider reload; substrate-wide fix explicitly out-of-scope).
    //  10. get_oauth_token_passes_client_secret_when_set — GH #233 (2026-05-21):
    //      when Settings.ClientSecret is non-empty (MAL App Type "web" — confidential PKCE),
    //      the provider passes it through to the proxy's ExchangeCodeForToken call.
    //  11. get_oauth_token_omits_client_secret_when_empty — GH #233 back-compat:
    //      when Settings.ClientSecret is empty (MAL App Type "Other" — public-client PKCE),
    //      the provider passes a null/empty value through; the proxy's separate unit
    //      coverage (MalImportListProxyFixture) verifies the form body does NOT include
    //      the client_secret parameter.
    //  12. RefreshToken_passes_client_secret_when_set — GH #233: refresh leg also carries
    //      Settings.ClientSecret through to the proxy.
    //  13. RefreshToken_omits_client_secret_when_empty — GH #233 back-compat: refresh
    //      leg works without ClientSecret for App Type "Other" users.
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

            // GH #231: the Option A reload path looks up the persisted Definition. Return a
            // Definition whose Settings carry the same PendingPkceState so the in-flight value
            // is preserved and the happy-path exchange proceeds (this also documents the
            // expected contract — a stub Definition that mirrors the in-flight state is the
            // normal post-startOAuth shape).
            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = new MalImportListSettings
                     {
                         ClientId = "fixture-client-id",
                         PendingPkceState = _settings.PendingPkceState
                     }
                 });

            // GH #233: the proxy signature now carries (clientId, clientSecret, code, verifier).
            // Setup matches the no-secret case (Settings.ClientSecret default = null) so the
            // happy-path Test 3 continues to assert the original behaviour while exercising the
            // new 4-arg shape.
            _proxy.Setup(p => p.ExchangeCodeForToken("fixture-client-id", null, "auth-code-fixture", pending.Verifier))
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

            // GH #231 reload: return a Definition with the same persisted PendingPkceState
            // so we exercise the rejection path AFTER the reload (not a side-effect of the
            // reload itself overwriting the value).
            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = new MalImportListSettings
                     {
                         ClientId = "fixture-client-id",
                         PendingPkceState = _settings.PendingPkceState
                     }
                 });

            // Attacker swaps the state nonce while keeping the code.
            var maliciousUrl = "https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state=ATTACKER_FORGED_NONCE";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", maliciousUrl } });

            // Tokens MUST NOT be exchanged.
            _proxy.Verify(
                p => p.ExchangeCodeForToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
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

            // GH #231 reload: return Definition with the same expired blob so the reload
            // doesn't suppress the expiry-path under test.
            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = new MalImportListSettings
                     {
                         ClientId = "fixture-client-id",
                         PendingPkceState = _settings.PendingPkceState
                     }
                 });

            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            _proxy.Verify(
                p => p.ExchangeCodeForToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
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

            // GH #233: proxy signature now (clientId, clientSecret, refreshToken). Default
            // ClientSecret=null exercises the back-compat (Other App Type) path.
            _proxy.Setup(p => p.RefreshAccessToken("fixture-client-id", null, "old-refresh"))
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
                p => p.RefreshAccessToken("fixture-client-id", null, "old-refresh"),
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
            _proxy.Setup(p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
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
                p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
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

            _proxy.Setup(p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "post-401-access",
                      RefreshToken = "post-401-refresh",
                      ExpiresIn = 2592000
                  });

            var result = Subject.Fetch();

            result.Should().NotBeNull();
            _proxy.Verify(
                p => p.RefreshAccessToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Once,
                "D-05 reactive 401-retry: after the initial 401, the provider must force-expire Settings.Expires and call RefreshTokenIfNecessary which dispatches RefreshAccessToken.");

            _settings.AccessToken.Should().Be("post-401-access",
                "the post-401 refresh must persist the new access token.");
        }

        // ── Test 9: GH #231 — reload PendingPkceState from repository before validation ──
        [Test]
        public void get_oauth_token_reloads_pending_pkce_state_from_repository()
        {
            // Simulate the post-SchemaBuilder.ReadFromSchema wipe: the FE form-state
            // round-trip carried pendingPkceState="" (the field is Hidden-without-Privacy,
            // so the substrate's preserve-existing branch did NOT fire — see
            // MalImportListSettings.cs comment block on the PendingPkceState field).
            _settings.PendingPkceState = string.Empty;

            // DB DOES carry a valid blob — the persisted MalOAuthState from the most-recent
            // startOAuth call. Without the GH #231 reload, this value would never reach the
            // in-flight Settings POCO and the validation would short-circuit on the empty-check.
            var pending = MalOAuthState.Create();
            var persistedSettings = new MalImportListSettings
            {
                ClientId = "fixture-client-id",
                PendingPkceState = JsonConvert.SerializeObject(pending)
            };

            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = persistedSettings
                 });

            // Happy-path proxy: validates state, exchanges code+verifier for tokens.
            _proxy.Setup(p => p.ExchangeCodeForToken("fixture-client-id", null, "auth-code-fixture", pending.Verifier))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "post-reload-access",
                      RefreshToken = "post-reload-refresh",
                      ExpiresIn = 2592000
                  });

            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            // The repository MUST have been queried for the persisted Definition.
            _repo.Verify(
                r => r.Get(9012),
                Times.Once,
                "GH #231 Option A reload: the provider must invoke _importListRepository.Get to " +
                "restore the DB-persisted PendingPkceState before the empty-check fires.");

            // The validation MUST have proceeded past the empty-check — proven by the proxy
            // exchange call being dispatched with the persisted Verifier.
            _proxy.Verify(
                p => p.ExchangeCodeForToken("fixture-client-id", null, "auth-code-fixture", pending.Verifier),
                Times.Once,
                "GH #231 fix: after the reload restores PendingPkceState, validation must proceed " +
                "past the empty-check, deserialize the persisted MalOAuthState, validate the state " +
                "nonce, and exchange the code+verifier for tokens. Without the reload, " +
                "Settings.PendingPkceState would be empty and the request would short-circuit " +
                "with NoPendingPkceState.");

            // Tokens persisted on Settings.
            _settings.AccessToken.Should().Be("post-reload-access");

            // Single-use clear must still apply after a successful exchange.
            _settings.PendingPkceState.Should().BeNull(
                "T-V11 single-use: even via the reload path, PendingPkceState must be cleared on " +
                "successful exchange so a replay of the same callback URL fails.");

            // Result envelope is the normal happy-path shape.
            result.Should().NotBeNull();
        }

        // ── Test 10: GH #233 — provider passes ClientSecret through on exchange ────────
        [Test]
        public void get_oauth_token_passes_client_secret_when_set()
        {
            // MAL App Type "web" (confidential PKCE) — user supplied a client_secret.
            _settings.ClientSecret = "fixture-mal-web-client-secret";

            var pending = MalOAuthState.Create();
            _settings.PendingPkceState = JsonConvert.SerializeObject(pending);

            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = new MalImportListSettings
                     {
                         ClientId = "fixture-client-id",
                         ClientSecret = "fixture-mal-web-client-secret",
                         PendingPkceState = _settings.PendingPkceState
                     }
                 });

            // The proxy MUST receive the client_secret as the 2nd positional arg. If the
            // provider passes null/empty here, the MAL App Type "web" flow returns 401 from
            // MAL — that's the GH #233 regression class this test pins.
            _proxy.Setup(p => p.ExchangeCodeForToken(
                              "fixture-client-id",
                              "fixture-mal-web-client-secret",
                              "auth-code-fixture",
                              pending.Verifier))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "web-flow-access",
                      RefreshToken = "web-flow-refresh",
                      ExpiresIn = 2592000
                  });

            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            var result = Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            // The provider MUST have called the proxy with the client_secret value passed
            // through verbatim. Strict-arg matching pins the GH #233 contract.
            _proxy.Verify(
                p => p.ExchangeCodeForToken(
                    "fixture-client-id",
                    "fixture-mal-web-client-secret",
                    "auth-code-fixture",
                    pending.Verifier),
                Times.Once,
                "GH #233: MAL App Type 'web' requires client_secret in the token-exchange form body; " +
                "the provider MUST pass Settings.ClientSecret through to the proxy's 2nd positional arg.");

            _settings.AccessToken.Should().Be("web-flow-access",
                "happy path: tokens persisted as normal post-exchange.");

            // T-V7: response envelope must NOT echo the client_secret.
            result.Should().NotBeNull();
            var json = JsonConvert.SerializeObject(result);
            json.Should().NotContain("fixture-mal-web-client-secret",
                "T-V7: the response envelope from getOAuthToken must NEVER echo the client_secret.");
        }

        // ── Test 11: GH #233 back-compat — ClientSecret empty on exchange (Other App Type) ──
        [Test]
        public void get_oauth_token_omits_client_secret_when_empty()
        {
            // MAL App Type "Other" (public-client PKCE) — user left ClientSecret blank.
            _settings.ClientSecret = null;

            var pending = MalOAuthState.Create();
            _settings.PendingPkceState = JsonConvert.SerializeObject(pending);

            _repo.Setup(r => r.Get(9012))
                 .Returns(new ImportListDefinition
                 {
                     Id = 9012,
                     Settings = new MalImportListSettings
                     {
                         ClientId = "fixture-client-id",
                         ClientSecret = null,
                         PendingPkceState = _settings.PendingPkceState
                     }
                 });

            _proxy.Setup(p => p.ExchangeCodeForToken(
                              "fixture-client-id",
                              null,
                              "auth-code-fixture",
                              pending.Verifier))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "other-flow-access",
                      RefreshToken = "other-flow-refresh",
                      ExpiresIn = 2592000
                  });

            var redirectedUrl = $"https://mangarr.local/oauth/mal/callback?code=auth-code-fixture&state={pending.StateNonce}";

            Subject.RequestAction(
                "getOAuthToken",
                new Dictionary<string, string> { { "redirectedUrl", redirectedUrl } });

            // The provider MUST have called the proxy with null/empty for client_secret. The
            // proxy's separate coverage (MalImportListProxyFixture) verifies that the form
            // body does NOT include the client_secret parameter at all in that branch — the
            // wire-level back-compat with MAL App Type "Other" depends on it.
            _proxy.Verify(
                p => p.ExchangeCodeForToken(
                    "fixture-client-id",
                    null,
                    "auth-code-fixture",
                    pending.Verifier),
                Times.Once,
                "GH #233 back-compat: MAL App Type 'Other' (public-client PKCE) users leave Settings.ClientSecret blank; " +
                "the provider MUST pass null/empty through unchanged so the proxy omits the client_secret form parameter.");

            _settings.AccessToken.Should().Be("other-flow-access",
                "happy path: tokens persisted as normal post-exchange (Other App Type still works).");
        }

        // ── Test 12: GH #233 — RefreshToken passes ClientSecret through ───────────────
        [Test]
        public void RefreshToken_passes_client_secret_when_set()
        {
            // MAL App Type "web" — refresh leg also needs the client_secret per MAL OAuth docs.
            _settings.ClientSecret = "fixture-mal-web-client-secret";
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // inside 5-min lookahead
            _settings.AccessToken = "old-web-access";
            _settings.RefreshToken = "old-web-refresh";

            _proxy.Setup(p => p.RefreshAccessToken(
                              "fixture-client-id",
                              "fixture-mal-web-client-secret",
                              "old-web-refresh"))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "rotated-web-access",
                      RefreshToken = "rotated-web-refresh",
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
                p => p.RefreshAccessToken(
                    "fixture-client-id",
                    "fixture-mal-web-client-secret",
                    "old-web-refresh"),
                Times.Once,
                "GH #233: the refresh-token leg ALSO requires client_secret for MAL App Type 'web' per " +
                "MAL OAuth docs (https://myanimelist.net/blog.php?eid=835707). The provider MUST pass " +
                "Settings.ClientSecret through to the proxy's RefreshAccessToken 2nd positional arg.");

            _settings.AccessToken.Should().Be("rotated-web-access",
                "rotated access-token must be persisted on Settings after the App Type 'web' refresh.");
        }

        // ── Test 13: GH #233 back-compat — RefreshToken omits ClientSecret when empty ─
        [Test]
        public void RefreshToken_omits_client_secret_when_empty()
        {
            // MAL App Type "Other" — refresh works without client_secret.
            _settings.ClientSecret = null;
            _settings.Expires = DateTime.UtcNow.AddSeconds(30); // inside 5-min lookahead
            _settings.AccessToken = "old-other-access";
            _settings.RefreshToken = "old-other-refresh";

            _proxy.Setup(p => p.RefreshAccessToken(
                              "fixture-client-id",
                              null,
                              "old-other-refresh"))
                  .Returns(new MalTokenResponse
                  {
                      AccessToken = "rotated-other-access",
                      RefreshToken = "rotated-other-refresh",
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
                p => p.RefreshAccessToken(
                    "fixture-client-id",
                    null,
                    "old-other-refresh"),
                Times.Once,
                "GH #233 back-compat: refresh works for MAL App Type 'Other' (public-client PKCE) " +
                "with null client_secret. The provider MUST pass null/empty through unchanged so the " +
                "proxy omits the client_secret form parameter — wire-level back-compat with MAL " +
                "depends on it.");

            _settings.AccessToken.Should().Be("rotated-other-access",
                "rotated access-token must be persisted on Settings after the App Type 'Other' refresh.");
        }
    }
}
