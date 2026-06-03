using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 / Phase 39 Plan 39-07 — req-axis DOWNLOAD-01 (the default
/// DownloadClient). Rewritten from DownloadClientsInProcessFixture: Phase 39 retired
/// the in-process image downloader (Plan 39-02), so GatewayDownloadClient is now the
/// sole download client seeded by SeedBaselineAsync.
///
/// Hits GET /api/v5/downloadclient via the Playwright APIRequest context, asserts the
/// gateway client (seeded by SeedBaselineAsync) is present in the response, and
/// enable=true. This is the canonical req-axis test: the GatewayDownloadClient runs as
/// the default download client on every fresh DB.
///
/// Tier (D-04): req axis = PR-smoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class DownloadClientsGatewayFixture : AutomationTest
{
    [Test]
    public async Task gateway_client_is_default()
    {
        // Page.APIRequest is an IAPIRequestContext bound to the active browser
        // context (which carries X-Api-Key via ExtraHTTPHeaders set in
        // AutomationTest.OneTimeSetUpAsync), so it shares the auth state and
        // can hit the API directly without spinning up a new request context.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/downloadclient");
        listResp.Status.Should().Be(200);

        var bodyText = await listResp.TextAsync();
        bodyText.Should().Contain("GatewayDownloadClient");
        bodyText.Should().MatchRegex("\"enable\"\\s*:\\s*true");
    }
}
