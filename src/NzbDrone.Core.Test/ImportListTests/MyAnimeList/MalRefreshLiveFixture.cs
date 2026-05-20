using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Core.Test.ImportListTests.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 3 — LIVE-NETWORK contract probe for MAL refresh-token grant.
    //
    // Per Phase 20 D-09/D-10 LiveService policy:
    //   * [Category("LiveService")] — excluded from default unit + integration runs.
    //   * Opt-in via env var MAL_LIVE_REFRESH_TOKEN; skip with Assert.Inconclusive when
    //     unset so default CI is unaffected.
    //   * Paired GH issue tracks live cadence: title
    //     "[live-service-probe] Track MAL OAuth refresh-token live cadence", label
    //     `live-service-probe`. Issue URL recorded in 27-04-SUMMARY.md.
    //
    // Rationale (CONTEXT lines 180-183 + 27-04-PLAN Test 9):
    //   * MAL refresh-rotation cassettes are NOT feasible — every grant call rotates the
    //     refresh token, so a recorded cassette would only ever replay a single call before
    //     the next refresh fails with `400 invalid_grant` (the test would self-destruct
    //     after one run).
    //   * Live probe validates wire format + endpoint stability without locking in a
    //     captured refresh token. Direct HTTPS bypassing Mangarr's IHttpClient — mirrors
    //     the Plan 18 D-10 MangaDexLookupLiveFixture pattern.
    //
    // T-V7: refresh token is read from an env var and NEVER logged. Assertions check
    // structural shape only (access_token presence + length + expires_in > 0); they do
    // NOT print token values.
    //
    // Side-effect WARNING: this probe consumes ONE refresh-rotation cycle every time it
    // runs successfully. MAL rotates the refresh token on each grant call, so the env var
    // MUST be updated with the new refresh token from the response after each successful
    // run (otherwise the next run fails with 400 invalid_grant). Paired GH issue tracks
    // the cadence — recommend manual runs only, not nightly CI.
    [TestFixture]
    [Category("LiveService")]
    public class MalRefreshLiveFixture
    {
        // Mangarr's MAL OAuth ClientId — public per OAuth2 public-client semantics. MUST
        // match the value compiled into MalConstants.ClientId at release-build time. For
        // dev runs, set env var MAL_LIVE_CLIENT_ID to override (so executors testing pre-
        // release builds can use a personal MAL app).
        private static string ResolveClientId()
        {
            return Environment.GetEnvironmentVariable("MAL_LIVE_CLIENT_ID") ?? "REPLACE_AT_RELEASE_BUILD_WITH_MAL_PINNED_CLIENT_ID";
        }

        [Test]
        public async Task refresh_against_live_mal_endpoint_succeeds()
        {
            var refreshToken = Environment.GetEnvironmentVariable("MAL_LIVE_REFRESH_TOKEN");
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Assert.Inconclusive(
                    "MAL_LIVE_REFRESH_TOKEN env var is not set. Skipping per Phase 20 D-09 LiveService opt-in policy. " +
                    "To exercise this probe locally: register a MAL OAuth app at https://myanimelist.net/apiconfig, " +
                    "run the full OAuth flow once to obtain a refresh token, set MAL_LIVE_REFRESH_TOKEN=<token> " +
                    "(and optionally MAL_LIVE_CLIENT_ID for non-release builds), then run " +
                    "`dotnet test --filter \"Category=LiveService&FullyQualifiedName~MalRefreshLiveFixture\"`. " +
                    "NOTE: MAL rotates refresh tokens — update MAL_LIVE_REFRESH_TOKEN with the new value from " +
                    "the response after each successful run.");
                return;
            }

            var clientId = ResolveClientId();

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mangarr-CI/1.0 (https://github.com/devbrian/Mangarr; LiveService MAL refresh-token cadence probe)");
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
            });

            var response = await http.PostAsync("https://myanimelist.net/v1/oauth2/token", content);
            response.IsSuccessStatusCode.Should().BeTrue(
                "MAL must respond 200 on a refresh-token grant when the inputs are valid (HTTP status: " + (int)response.StatusCode + " " + response.StatusCode + ")");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            doc.RootElement.TryGetProperty("access_token", out var accessTokenElement)
                .Should().BeTrue("MAL response must contain an access_token field.");
            var accessToken = accessTokenElement.GetString();
            accessToken.Should().NotBeNullOrWhiteSpace(
                "the refresh grant must return a fresh access_token on success.");
            accessToken.Length.Should().BeGreaterThan(20,
                "MAL access tokens are JWT-shaped and exceed 20 chars.");

            doc.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                .Should().BeTrue("MAL response must contain an expires_in field.");
            expiresInElement.GetInt32().Should().BeGreaterThan(0,
                "MAL currently issues ~2,592,000 / 30 days expires_in.");

            doc.RootElement.TryGetProperty("refresh_token", out _).Should().BeTrue(
                "MAL rotates refresh tokens — response must include the rotated value (caller must persist it; reusing the OLD refresh token will 400 on the next grant).");
        }
    }
}
