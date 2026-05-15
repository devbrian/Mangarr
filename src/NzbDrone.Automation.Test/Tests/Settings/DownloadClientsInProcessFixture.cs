using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — req-axis DOWNLOAD-01 (InProcess is default DownloadClient).
///
/// Hits GET /api/v5/downloadclient via the Playwright APIRequest context, asserts
/// the InProcess client (seeded by SeedBaselineAsync L86-101) is present in the
/// response, and enabled=true. This is the canonical req-axis test: the InProcess
/// downloader runs as default download client on every fresh DB.
///
/// Tier (D-04): req axis = PR-smoke.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class DownloadClientsInProcessFixture : AutomationTest
{
    [Test]
    public async Task inprocess_client_is_default()
    {
        // Page.APIRequest is an IAPIRequestContext bound to the active browser
        // context (which carries X-Api-Key via ExtraHTTPHeaders set in
        // AutomationTest.OneTimeSetUpAsync L90), so it shares the auth state and
        // can hit the API directly without spinning up a new request context.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/downloadclient");
        listResp.Status.Should().Be(200);

        var bodyText = await listResp.TextAsync();
        bodyText.Should().Contain("InProcessImageDownloadClient");
        bodyText.Should().MatchRegex("\"enable\"\\s*:\\s*true");
    }
}
