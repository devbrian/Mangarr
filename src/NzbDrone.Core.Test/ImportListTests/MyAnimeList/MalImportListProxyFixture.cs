using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.MyAnimeList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeList
{
    // GH #233 (2026-05-21) — wire-level coverage for MalImportListProxy.
    //
    // The proxy carries the conditional client_secret form-parameter logic for both
    // ExchangeCodeForToken and RefreshAccessToken. These tests intercept the
    // IHttpClient.Post(HttpRequest) call, decode the urlencoded form body from
    // request.ContentData, and assert on the presence/absence of `client_secret=`.
    //
    // Why decode ContentData directly: HttpRequestBuilder.ApplyFormData serializes the
    // form parameters to UTF-8 `application/x-www-form-urlencoded` bytes via
    // string.Join("&", parameters) at HttpRequestBuilder.cs:230-232. The
    // proxy-under-test calls _httpClient.Post(request) on a request whose ContentData
    // is the rendered body — decoding that string is the canonical way to verify the
    // wire shape without a live network call.
    //
    // Coverage pairs (4 tests):
    //   * ExchangeCodeForToken — secret set    ⇒ body CONTAINS `client_secret=...`
    //   * ExchangeCodeForToken — secret empty  ⇒ body does NOT contain `client_secret`
    //   * RefreshAccessToken   — secret set    ⇒ body CONTAINS `client_secret=...`
    //   * RefreshAccessToken   — secret empty  ⇒ body does NOT contain `client_secret`
    //
    // T-V7: the test secret values use opaque fixture strings (NOT real-looking
    // base64 tokens). No real credentials reach a test file.
    [TestFixture]
    public class MalImportListProxyFixture : CoreTest<MalImportListProxy>
    {
        private Mock<IHttpClient> _httpClient;
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _httpClient = Mocker.GetMock<IHttpClient>();
            _capturedRequest = null;

            // Intercept the Post call so we can inspect the encoded form body. Return a
            // canned 200 token response so the proxy's deserialize path doesn't trip.
            _httpClient.Setup(c => c.Post(It.IsAny<HttpRequest>()))
                       .Callback<HttpRequest>(req => _capturedRequest = req)
                       .Returns<HttpRequest>(req => new HttpResponse(
                           req,
                           new HttpHeader { ContentType = "application/json" },
                           "{\"token_type\":\"Bearer\",\"expires_in\":2592000,\"access_token\":\"FIXTURE_ACCESS\",\"refresh_token\":\"FIXTURE_REFRESH\"}",
                           HttpStatusCode.OK));
        }

        private static string DecodeFormBody(HttpRequest request)
        {
            request.Should().NotBeNull("the proxy must have called IHttpClient.Post exactly once.");
            request.ContentData.Should().NotBeNull("HttpRequestBuilder.ApplyFormData must have rendered the form parameters into ContentData.");
            return Encoding.UTF8.GetString(request.ContentData);
        }

        // ── ExchangeCodeForToken — client_secret set (MAL App Type "web") ─────────────
        [Test]
        public void exchange_code_for_token_includes_client_secret_when_set()
        {
            var response = Subject.ExchangeCodeForToken(
                clientId: "fixture-client-id",
                clientSecret: "fixture-mal-web-client-secret",
                code: "fixture-code",
                verifier: "fixture-verifier");

            response.Should().NotBeNull();

            var body = DecodeFormBody(_capturedRequest);
            body.Should().Contain("client_secret=fixture-mal-web-client-secret",
                "GH #233: when MalImportListSettings.ClientSecret is non-empty (MAL App Type 'web'), " +
                "the proxy MUST include `client_secret={value}` in the urlencoded form body or MAL " +
                "returns 401 invalid_client at https://myanimelist.net/v1/oauth2/token.");

            // Sanity: the other expected params are also present (no regression).
            body.Should().Contain("grant_type=authorization_code");
            body.Should().Contain("client_id=fixture-client-id");
            body.Should().Contain("code=fixture-code");
            body.Should().Contain("code_verifier=fixture-verifier");
        }

        // ── ExchangeCodeForToken — client_secret empty (MAL App Type "Other") ─────────
        [Test]
        public void exchange_code_for_token_omits_client_secret_when_empty()
        {
            var response = Subject.ExchangeCodeForToken(
                clientId: "fixture-client-id",
                clientSecret: null,
                code: "fixture-code",
                verifier: "fixture-verifier");

            response.Should().NotBeNull();

            var body = DecodeFormBody(_capturedRequest);
            body.Should().NotContain("client_secret",
                "GH #233 back-compat: when ClientSecret is null/empty/whitespace (MAL App Type 'Other'), " +
                "the proxy MUST OMIT the client_secret form parameter entirely. MAL App Type 'Other' " +
                "is public-client PKCE; sending an unknown client_secret would itself trip a 400/401.");

            // Sanity: PKCE verifier is still present (MAL App Type Other still needs it).
            body.Should().Contain("grant_type=authorization_code");
            body.Should().Contain("client_id=fixture-client-id");
            body.Should().Contain("code=fixture-code");
            body.Should().Contain("code_verifier=fixture-verifier");
        }

        // ── RefreshAccessToken — client_secret set (MAL App Type "web") ───────────────
        [Test]
        public void refresh_access_token_includes_client_secret_when_set()
        {
            var response = Subject.RefreshAccessToken(
                clientId: "fixture-client-id",
                clientSecret: "fixture-mal-web-client-secret",
                refreshToken: "fixture-refresh");

            response.Should().NotBeNull();

            var body = DecodeFormBody(_capturedRequest);
            body.Should().Contain("client_secret=fixture-mal-web-client-secret",
                "GH #233: the refresh-token leg of MAL App Type 'web' also requires client_secret " +
                "per https://myanimelist.net/blog.php?eid=835707. Without it MAL returns 401 " +
                "invalid_client and Mangarr can never rotate the access token for a 'web' client.");

            body.Should().Contain("grant_type=refresh_token");
            body.Should().Contain("client_id=fixture-client-id");
            body.Should().Contain("refresh_token=fixture-refresh");
        }

        // ── RefreshAccessToken — client_secret empty (MAL App Type "Other") ───────────
        [Test]
        public void refresh_access_token_omits_client_secret_when_empty()
        {
            var response = Subject.RefreshAccessToken(
                clientId: "fixture-client-id",
                clientSecret: null,
                refreshToken: "fixture-refresh");

            response.Should().NotBeNull();

            var body = DecodeFormBody(_capturedRequest);
            body.Should().NotContain("client_secret",
                "GH #233 back-compat: refresh-token grant for MAL App Type 'Other' (public-client " +
                "PKCE) MUST omit the client_secret form parameter; sending an unknown secret would " +
                "fail MAL's request validation.");

            body.Should().Contain("grant_type=refresh_token");
            body.Should().Contain("client_id=fixture-client-id");
            body.Should().Contain("refresh_token=fixture-refresh");
        }

        // ── Defense-in-depth: whitespace-only ClientSecret still omits the parameter ──
        [Test]
        public void exchange_code_for_token_omits_client_secret_when_whitespace_only()
        {
            // Defensive — if the user pastes whitespace by mistake, treat it as empty so
            // we don't send an obviously-invalid client_secret to MAL.
            Subject.ExchangeCodeForToken(
                clientId: "fixture-client-id",
                clientSecret: "   \t  ",
                code: "fixture-code",
                verifier: "fixture-verifier");

            var body = DecodeFormBody(_capturedRequest);
            body.Should().NotContain("client_secret",
                "GH #233 hardening: whitespace-only ClientSecret is treated as empty so a " +
                "fat-finger paste doesn't lock the user out with a confusing 401 from MAL.");
        }

        // ── Defense-in-depth: AuthorizeUrl never carries client_secret regardless ─────
        [Test]
        public void build_authorize_url_never_includes_client_secret()
        {
            var state = MalOAuthState.Create();

            var url = Subject.BuildAuthorizeUrl("fixture-client-id", state);

            url.Should().NotBeNullOrEmpty();
            url.Should().NotContain("client_secret",
                "MAL's OAuth2 authorize endpoint never accepts client_secret — only token-exchange + " +
                "refresh-token endpoints do, even for App Type 'web'. Including client_secret in the " +
                "authorize URL would leak the secret into the user's browser history (T-V7 violation).");

            url.Should().Contain("client_id=fixture-client-id");
            url.Should().Contain("code_challenge_method=plain");
            url.Should().Contain($"state={state.StateNonce}");
        }
    }
}
