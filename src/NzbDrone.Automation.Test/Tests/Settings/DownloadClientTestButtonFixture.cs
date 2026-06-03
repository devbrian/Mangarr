using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 / Phase 39 Plan 39-07 — DownloadClient Test-button coverage
/// (INVENTORY v5-endpoint row: POST /api/v5/downloadclient/test).
///
/// REDESIGNED for the gateway client. The original premise ("the in-process Test()
/// makes ZERO HTTP calls, so the endpoint is cassette-free") is dead — the in-process
/// downloader was retired in Plan 39-02. The GatewayDownloadClient Test() DOES make a
/// real HTTP call to the (non-existent) gateway host, so it returns a failed/rejected
/// result rather than succeeding. The meaningful assertion is therefore that the test
/// endpoint ROUND-TRIPS (returns one of {200, 400} — the gateway is unreachable in the
/// harness, which surfaces as a 400 validation failure or a 200 carrying a failed test
/// result), NOT that Test() succeeds.
///
/// The AutomationTest base seeds the baseline "Gateway (test seed)" client via
/// SeedBaselineAsync (with ?skipTesting=true so the seed itself never fires Test()), so
/// the card render is deterministic without extra seeding.
///
/// Tier (D-04): AutomationTest (Nightly), NOT LiveService — the gateway host is
/// localhost:8080 (unreachable in-harness), so no live external service is hit.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DownloadClientTestButtonFixture : AutomationTest
{
    [Test]
    public async Task test_button_round_trips()
    {
        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        var card = page.CardByName("Gateway (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditDownloadClientModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/test") && r.Request.Method == "POST",
            new() { Timeout = 30_000 });
        await modal.TestButton.ClickAsync();
        var resp = await postTask;

        // STATE assertion: the test endpoint round-trips. The gateway host is
        // unreachable in the harness, so the result is a reachability failure — a 400
        // validation rejection (the canonical "Test() failed" path) or a 200/204
        // carrying a failed test result. Either proves the endpoint is wired and the
        // client's Test() path executes end-to-end.
        resp.Status.Should().BeOneOf(200, 204, 400);
    }
}
