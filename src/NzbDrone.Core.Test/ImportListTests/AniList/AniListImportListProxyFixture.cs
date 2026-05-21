using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.AniList;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests.AniList
{
    // GH #230 (2026-05-21 live smoke) — AniListImportListProxy.GetPinAuthorizeUrl URL contract.
    //
    // The previous implementation pointed the FE's new tab at /api/v2/oauth/pin directly —
    // but that endpoint is the REDIRECT TARGET, not the authorize endpoint. AniList's pin
    // page reads the auth code from ITS OWN ?code=... query string after the authorize
    // redirect; with no redirect happening, the page renders the literal string "undefined".
    //
    // Fixed shape: hit /api/v2/oauth/authorize with redirect_uri=<pin-url>; AniList then
    // redirects to the pin page with ?code=<grant> appended, and the pin page extracts the
    // code from its own query string and displays it in the textbox.
    //
    // This is a PURE STRING-CONSTRUCTION test — no HTTP, no mocking. The proxy ctor takes
    // IHttpClient + Logger but neither is touched by GetPinAuthorizeUrl.
    [TestFixture]
    public class AniListImportListProxyFixture : CoreTest<AniListImportListProxy>
    {
        // ── Test 1: GH #230 happy path ───────────────────────────────────────────────
        [Test]
        public void get_pin_authorize_url_uses_authorize_endpoint_with_pin_redirect()
        {
            var settings = new AniListImportListSettings
            {
                ClientId = "41944"
            };

            var url = Subject.GetPinAuthorizeUrl(settings);

            // Must target the authorize endpoint — NOT the pin endpoint.
            url.Should().StartWith("https://anilist.co/api/v2/oauth/authorize?",
                "GH #230 fix: the previous URL pointed at /api/v2/oauth/pin which is the " +
                "REDIRECT TARGET, not the authorize endpoint. AniList's pin page only renders " +
                "a usable code if it was reached via redirect from authorize.");

            // ClientId must be propagated verbatim.
            url.Should().Contain("client_id=41944");

            // response_type=code is required by the OAuth2 authorization-code grant.
            url.Should().Contain("response_type=code");

            // redirect_uri MUST be the URL-encoded pin endpoint — AniList redirects to this
            // URL with ?code=... appended on success.
            url.Should().Contain("redirect_uri=https%3A%2F%2Fanilist.co%2Fapi%2Fv2%2Foauth%2Fpin",
                "GH #230 fix: redirect_uri must be the URL-encoded pin endpoint so AniList " +
                "appends ?code=... to that URL after consent.");
        }

        // ── Test 2: pre-save fallback (empty ClientId) ───────────────────────────────
        [Test]
        public void get_pin_authorize_url_handles_missing_client_id()
        {
            var settings = new AniListImportListSettings
            {
                ClientId = null
            };

            var url = Subject.GetPinAuthorizeUrl(settings);

            url.Should().Contain("client_id=",
                "the proxy must emit a partial URL even when ClientId is null/empty so the " +
                "FE can surface a Sonarr-shaped validation error rather than crash inside the proxy");
            url.Should().Contain("redirect_uri=https%3A%2F%2Fanilist.co%2Fapi%2Fv2%2Foauth%2Fpin");
        }
    }
}
