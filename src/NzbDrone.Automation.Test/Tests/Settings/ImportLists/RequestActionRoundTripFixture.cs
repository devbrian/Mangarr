using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using JsonNode = System.Text.Json.Nodes.JsonNode;
using JsonObject = System.Text.Json.Nodes.JsonObject;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 27 Plan 27-05 Task 1 — cross-provider RequestAction route round-trip.
//
// PURPOSE
// Proves the canonical V5 RequestAction surface (`POST /api/v5/importlist/action/{actionName}`)
// invokes each provider's `RequestAction(action, query)` override end-to-end:
//   * MangaDex: action="startOAuth" → D-08 internal-only password-grant probe;
//   * AniList:  action="startOAuth" → D-07 paste-pin authorize URL construction (deterministic, no outbound);
//   * MAL:      action="startOAuth" → D-09 paste-callback-URL authorize URL construction (deterministic, no outbound).
//
// Per Plan 27-05 Task 1 acceptance criteria: 3 NUnit test methods, one per provider, all GREEN.
//
// TEST-MODE / NO-LIVE-OAUTH DISCIPLINE
// AniList + MAL `startOAuth` actions are deterministic URL builders — they generate the
// authorize URL and (for MAL) persist transient PKCE state on the Settings POCO. Neither
// fires outbound HTTP. The route round-trip is a pure server-side test.
//
// MangaDex `startOAuth` IS outbound by design (D-08 verify-credentials probe against
// auth.mangadex.org Keycloak). With dummy credentials we expect MangaDex to return 4xx,
// which the provider catches via the HttpException-handler at MangaDexImportList.cs:108-118
// and returns the canonical `{ success: false, statusCode, error }` envelope. The V5
// controller wraps that envelope as 200 OK with JSON body — the V5 route ALWAYS returns
// 200 for an action that the provider handled (even if the provider's internal call
// failed); only ID-resolution or schema errors surface as 4xx at the controller tier.
// We assert the JSON envelope shape (success field present) — not the truthiness — so
// the test is robust to MangaDex API drift.
//
// Pattern source:
//   * Plan 27-02 SUMMARY §"Decisions Made" — RequestAction envelope shape contract.
//   * Plan 27-03 SUMMARY §"Decisions Made" — AniList paste-pin deterministic URL.
//   * Plan 27-04 SUMMARY §"Decisions Made" — MAL paste-URL deterministic + PKCE state persistence.
//   * src/NzbDrone.Automation.Test/Tests/Settings/ImportLists/ImportListCrudFixture.cs —
//     V5 RestSharp + X-Api-Key invocation pattern (TestKit base URL shape).
//
// Pattern κ enforcement (Phase 18 D-18): no `series-*`/`episode-*`/`season-*`/`add-series-*`
// selectors used; this fixture exercises the V5 API surface, not DOM testid selectors.
//
// T-27-05-V13 (Authentication): every request carries X-Api-Key per the ApiKey
// authentication scheme — same posture as every other Automation.Test fixture.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class RequestActionRoundTripFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        // Pitfall 10 / cross-fixture contract: Comix indexer is un-cassetted; mirror
        // sibling MangaDex/AniList/MAL ImportListSettingsFixture OneTimeSetUp.
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task mangadex_request_action_round_trip()
    {
        // D-08 internal-only password-grant probe — MangaDexImportList.RequestAction
        // ("startOAuth", query) calls IMangaDexImportListProxy.PasswordGrant with the
        // user-supplied credentials. With dummy values MangaDex returns 4xx; the
        // provider catches HttpException and returns the `{ success: false, statusCode, error }`
        // envelope. The V5 controller wraps that envelope as 200 OK + JSON body.
        var body = BuildMangaDexBody();
        var (status, payload) = await InvokeStartOAuthAsync(body);

        status.Should().Be(HttpStatusCode.OK,
            "V5 RequestAction route always returns 200 when the provider's RequestAction handler does not throw — Plan 27-02 D-08 envelope discipline.");

        payload.Should().NotBeNull("provider must return a JSON envelope");
        payload.AsObject().ContainsKey("success").Should().BeTrue(
            "MangaDexImportList.RequestAction('startOAuth') always returns an envelope carrying a `success` boolean (Plan 27-02 SUMMARY §'Decisions Made' — D-08 internal-only envelope shape).");

        // T-V7 token-leak invariant — neither AccessToken nor RefreshToken VALUES leak
        // back through the envelope. Plan 27-02 MangaDexImportListFixture test #1 proves
        // this at the unit tier; the route round-trip re-asserts the contract at the
        // serialized-body level (which is the wire-level surface). The check is
        // case-insensitive — Newtonsoft serializes anonymous-type properties as
        // camelCase, so a leaked `accessToken` key would slip past a PascalCase-only
        // exact-string check.
        var raw = payload.ToJsonString().ToLowerInvariant();
        raw.Should().NotContain("\"accesstoken\"", "Plan 27-02 T-V7 — token key/value never echoed through RequestAction envelope.");
        raw.Should().NotContain("\"refreshtoken\"", "Plan 27-02 T-V7 — refresh-token key/value never echoed through RequestAction envelope.");
    }

    [Test]
    public async Task anilist_request_action_round_trip()
    {
        // GH #230 — AniListImportListProxy.GetPinAuthorizeUrl returns
        // "https://anilist.co/api/v2/oauth/authorize?client_id={ClientId}&response_type=code&redirect_uri=https%3A%2F%2Fanilist.co%2Fapi%2Fv2%2Foauth%2Fpin"
        // VERBATIM (deterministic; no HTTP). Pre-#230 the URL was /pin?... directly, which made
        // AniList render the literal string "undefined" in the pin textbox (no redirect chain to
        // populate location.search.code). The authorize endpoint with redirect_uri=...pin is the
        // working contract.
        var body = BuildAniListBody();
        var (status, payload) = await InvokeStartOAuthAsync(body);

        status.Should().Be(HttpStatusCode.OK);

        payload.Should().NotBeNull();
        var json = payload.AsObject();
        json.ContainsKey("oauthUrl").Should().BeTrue(
            "AniListImportList.RequestAction('startOAuth') returns `{ OauthUrl: <authorize URL> }` per GH #230 fix.");

        var oauthUrl = json["oauthUrl"]?.GetValue<string>();
        oauthUrl.Should().NotBeNullOrWhiteSpace();
        oauthUrl.Should().StartWith("https://anilist.co/api/v2/oauth/authorize",
            "AniList authorize URL points at /authorize (GH #230 fix); pre-fix /pin URL rendered 'undefined' in the pin textbox.");
        oauthUrl.Should().Contain("redirect_uri=https%3A%2F%2Fanilist.co%2Fapi%2Fv2%2Foauth%2Fpin",
            "the authorize call MUST carry the pin URL as redirect_uri or AniList's pin page can't read the auth code from its own query string.");
        oauthUrl.Should().Contain("response_type=code",
            "RFC 6749 authorization-code-grant query param.");
    }

    [Test]
    public async Task mal_request_action_round_trip()
    {
        // D-09 paste-callback-URL — MalImportList.RequestAction("startOAuth") calls
        // MalOAuthState.Create() (CSPRNG nonce + verifier), persists the state blob on
        // Settings.PendingPkceState, and returns { OauthUrl: <authorize URL> } VERBATIM
        // with `code_challenge_method=plain` + state nonce. Plan 27-04 SUMMARY §"Decisions Made"
        // — deterministic + persists transient flow state (no outbound HTTP).
        //
        // Phase 27 post-merge round-2 fix: MAL fails fast when Definition.Id <= 0
        // (the PKCE state needs to round-trip across the startOAuth → getOAuthToken
        // boundary via the persisted Settings JSON column). Register a real MAL row
        // first so the action call sees a saved Definition.
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (_, definitionId) = await tk.RegisterMalImportListAsync("MyAnimeList (round-trip)");
        definitionId.Should().NotBeNull("RegisterMalImportListAsync must succeed in Phase 27+");

        var body = BuildMalBody();
        body["id"] = definitionId!.Value;
        var (status, payload) = await InvokeStartOAuthAsync(body);

        status.Should().Be(HttpStatusCode.OK);

        payload.Should().NotBeNull();
        var json = payload.AsObject();
        json.ContainsKey("oauthUrl").Should().BeTrue(
            "MalImportList.RequestAction('startOAuth') returns `{ OauthUrl: <authorize URL> }` per D-09 + Plan 27-04 SUMMARY.");

        var oauthUrl = json["oauthUrl"]?.GetValue<string>();
        oauthUrl.Should().NotBeNullOrWhiteSpace();
        oauthUrl.Should().StartWith("https://myanimelist.net/v1/oauth2/authorize",
            "MAL authorize URL is fixed per MalConstants.AuthorizeUrl + CONTEXT line 33-36.");
        oauthUrl.Should().Contain("code_challenge_method=plain",
            "Plan 27-04 PKCE-plain enforcement — MAL only accepts code_challenge_method=plain (NOT the hashed variant).");
        oauthUrl.Should().Contain("state=",
            "Plan 27-04 T-V11 CSRF defense — every authorize URL carries a freshly-generated 32-byte state nonce.");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<(HttpStatusCode Status, JsonNode Payload)> InvokeStartOAuthAsync(JsonObject body)
    {
        // Use RestSharp matching the TestKit pattern (statement-style; see TestKit.cs:1430-1435
        // BuildRequest helper). Base URL `{RootUri}/api/v5`; resource `importlist/action/startOAuth`.
        // X-Api-Key header carries the test-run API key.
        var client = new RestClient($"{RootUri}/api/v5");
        var request = new RestRequest("importlist/action/startOAuth", Method.POST);
        request.AddHeader("X-Api-Key", ApiKey);
        request.AddHeader("Accept", "application/json");
        request.AddJsonBody(body.ToJsonString());

        var response = await client.ExecuteAsync(request);

        JsonNode payload = null;
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            try
            {
                payload = JsonNode.Parse(response.Content);
            }
            catch (JsonException)
            {
                // Body is not JSON — leave payload null so the per-test assertion fails
                // with a useful "payload should not be null" message instead of a parse
                // crash inside the test framework.
            }
        }

        return (response.StatusCode, payload);
    }

    private static JsonObject BuildMangaDexBody()
    {
        // ProviderResource shape — Implementation discriminator drives the V5 factory
        // resolution. Settings carries the user-supplied credential block (dummy values
        // — the test does NOT depend on MangaDex accepting the credentials; it only
        // depends on the V5 route + the provider's RequestAction handler returning the
        // canonical envelope shape).
        return new JsonObject
        {
            ["id"] = 0,
            ["name"] = "MangaDex (round-trip)",
            ["implementation"] = "MangaDexImportList",
            ["configContract"] = "MangaDexImportListSettings",
            ["settings"] = new JsonObject
            {
                ["clientId"] = "phase-27-test-client-id",
                ["clientSecret"] = "phase-27-test-client-secret",
                ["username"] = "phase-27-test-user",
                ["password"] = "phase-27-test-password",
                ["accessToken"] = "",
                ["refreshToken"] = "",
                ["expires"] = "0001-01-01T00:00:00",
                ["authUser"] = "",
                ["signIn"] = "startOAuth",
            },
        };
    }

    private static JsonObject BuildAniListBody()
    {
        return new JsonObject
        {
            ["id"] = 0,
            ["name"] = "AniList (round-trip)",
            ["implementation"] = "AniListImportList",
            ["configContract"] = "AniListImportListSettings",
            ["settings"] = new JsonObject
            {
                ["clientId"] = "12345",
                ["clientSecret"] = "phase-27-test-client-secret",
                ["status"] = 0,
                ["accessToken"] = "",
                ["refreshToken"] = "",
                ["expires"] = "0001-01-01T00:00:00",
                ["authUser"] = "",
                ["signIn"] = "startOAuth",
            },
        };
    }

    private static JsonObject BuildMalBody()
    {
        // V5 controller reads the `fields` array (per Mangarr.Api.V5.Provider.ProviderResource.cs:11),
        // NOT a flat `settings` object. SchemaBuilder.ReadFromSchema maps each {name, value} pair
        // onto the Settings POCO field with the matching property name. The other providers in
        // this fixture omit `fields` and rely on default values being acceptable to their
        // RequestAction handlers — MAL's per-user-ClientId guard requires us to actually
        // populate Settings.ClientId server-side.
        return new JsonObject
        {
            ["id"] = 0,
            ["name"] = "MyAnimeList (round-trip)",
            ["implementation"] = "MalImportList",
            ["configContract"] = "MalImportListSettings",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "clientId",
                    ["value"] = "fixture-mal-client-id",
                },
            },
            ["settings"] = new JsonObject
            {
                ["status"] = 0,
                ["accessToken"] = "",
                ["refreshToken"] = "",
                ["expires"] = "0001-01-01T00:00:00",
                ["pendingPkceState"] = "",
                ["authUser"] = "",
                ["signIn"] = "startOAuth",
            },
        };
    }
}
